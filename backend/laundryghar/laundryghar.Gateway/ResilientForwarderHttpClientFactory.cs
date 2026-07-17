using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Yarp.ReverseProxy.Forwarder;

namespace laundryghar.Gateway;

/// <summary>
/// Adds a per-cluster circuit breaker + timeout + concurrency limiter to every proxied request.
///
/// <para>
/// YARP's default <see cref="ForwarderHttpClientFactory"/> builds a raw <see cref="HttpMessageInvoker"/>
/// around <see cref="System.Net.Http.SocketsHttpHandler"/> — it does NOT go through
/// <c>IHttpClientFactory</c>, so the <c>ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())</c>
/// baseline in <c>laundryghar.ServiceDefaults</c> (applied to every OTHER HttpClient in this
/// solution) never touches proxied traffic. Without this class, a hang in core/operations/commerce
/// would mean the gateway — the single production entry point — waits on it indefinitely for
/// every request routed to that cluster, with no bound on concurrent in-flight proxied calls.
/// </para>
///
/// <para>
/// Per <see href="https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/http-client-config#custom-iforwarderhttpclientfactory">
/// YARP's documented extension point</see>, this subclasses <see cref="ForwarderHttpClientFactory"/>
/// and overrides <see cref="ConfigureHandler"/> (bulkhead at the socket layer) and
/// <see cref="WrapHandler"/> (circuit breaker + timeout via a Polly pipeline). One pipeline per
/// cluster, cached for the process lifetime so breaker state survives YARP's periodic client
/// rebuilds (which happen whenever cluster config changes) — a new instance per rebuild would
/// silently reset an open circuit back to closed.
/// </para>
/// </summary>
public sealed class ResilientForwarderHttpClientFactory : ForwarderHttpClientFactory
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _pipelines = new();
    private readonly ILogger<ResilientForwarderHttpClientFactory> _logger;

    public ResilientForwarderHttpClientFactory(ILogger<ResilientForwarderHttpClientFactory> logger)
    {
        _logger = logger;
    }

    protected override void ConfigureHandler(ForwarderHttpClientContext context, SocketsHttpHandler handler)
    {
        base.ConfigureHandler(context, handler);

        // Bulkhead at the connection layer: caps sockets YARP will open to this cluster's
        // destination. Backstops the Polly concurrency limiter below (which rejects at the
        // request layer, before a socket is even requested) — belt and braces.
        handler.MaxConnectionsPerServer = 100;
    }

    protected override HttpMessageHandler WrapHandler(ForwarderHttpClientContext context, HttpMessageHandler handler)
    {
        var inner = base.WrapHandler(context, handler);
        var pipeline = _pipelines.GetOrAdd(context.ClusterId, BuildPipeline);
        return new ResilientDelegatingHandler(pipeline) { InnerHandler = inner };
    }

    private ResiliencePipeline<HttpResponseMessage> BuildPipeline(string clusterId) =>
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            // Concurrency limiter (bulkhead): at most 100 in-flight proxied requests to this
            // cluster at once. QueueLimit=0 — over the limit rejects immediately (502 to the
            // gateway's caller) rather than queuing, which is what actually stops pile-up.
            .AddConcurrencyLimiter(permitLimit: 100, queueLimit: 0)
            // Circuit breaker sits OUTSIDE the per-attempt timeout below (mirrors Microsoft's own
            // Standard Resilience Handler ordering: rate limiter -> ... -> circuit breaker ->
            // attempt timeout). This is required, not cosmetic: the timeout strategy throws
            // TimeoutRejectedException to whatever wraps IT — if the breaker were INSIDE the
            // timeout instead, it would only ever see the timeout's raw internal cancellation
            // (indistinguishable from a caller hanging up) and could never actually observe "this
            // dependency is too slow" as a failure.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                MinimumThroughput = 8,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(15),
                ShouldHandle = args => ValueTask.FromResult(ShouldTreatAsFailure(args.Outcome)),
                OnOpened = args =>
                {
                    _logger.LogError(
                        "Gateway circuit OPEN for cluster '{ClusterId}' — proxied requests will " +
                        "fail fast for the next {BreakDuration}s instead of waiting on it.",
                        clusterId, args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation(
                        "Gateway circuit CLOSED for cluster '{ClusterId}' — proxying resumed.",
                        clusterId);
                    return default;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation(
                        "Gateway circuit HALF-OPEN for cluster '{ClusterId}' — probing.", clusterId);
                    return default;
                },
            })
            // Per-attempt timeout: these are calls to OUR OWN backend hosts on the private
            // network, so latency should be low single-digit seconds even under load — a hang
            // past 10s means the destination is degraded, not just busy.
            .AddTimeout(TimeSpan.FromSeconds(10))
            .Build();

    /// <summary>
    /// Real dependency failure (counts toward the circuit breaker) vs. the caller simply hanging
    /// up (does NOT count — that's information about the caller, not the dependency's health).
    /// A <see cref="Polly.Timeout.TimeoutRejectedException"/> from OUR OWN per-attempt timeout IS
    /// a real failure signal ("this dependency is too slow"); a raw
    /// <see cref="OperationCanceledException"/> is what an externally-cancelled request (client
    /// disconnected, upstream request aborted) looks like and must NOT be conflated with it.
    /// </summary>
    private static bool ShouldTreatAsFailure(Outcome<HttpResponseMessage> outcome)
    {
        if (outcome.Exception is Polly.Timeout.TimeoutRejectedException) return true;
        if (outcome.Exception is OperationCanceledException) return false;
        if (outcome.Exception is not null) return true;
        return IsServerError(outcome.Result);
    }

    private static bool IsServerError(HttpResponseMessage? response) =>
        response is not null && (int)response.StatusCode >= 500;

    /// <summary>Runs the inner handler chain through the cluster's Polly pipeline.</summary>
    private sealed class ResilientDelegatingHandler : DelegatingHandler
    {
        private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

        public ResilientDelegatingHandler(ResiliencePipeline<HttpResponseMessage> pipeline)
        {
            _pipeline = pipeline;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return await _pipeline.ExecuteAsync(
                async ct => await base.SendAsync(request, ct),
                cancellationToken);
        }
    }
}
