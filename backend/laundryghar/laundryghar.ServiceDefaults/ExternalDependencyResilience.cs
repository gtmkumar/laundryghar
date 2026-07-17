using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Per-dependency circuit breaker + timeout + concurrency-limit tuning for named HttpClients
/// that call external services (payment gateways, SMS/WhatsApp/push providers, etc).
///
/// <para>
/// <see cref="Extensions.AddServiceDefaults{TBuilder}"/> already applies
/// <c>ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler())</c> to every
/// HttpClient in every host, as a baseline safety net. But the library's default thresholds
/// (~100-sample circuit-breaker window, ~1000-permit rate limiter) are tuned for high-volume
/// traffic — at our real external-call volumes (tens per poll cycle, or one-off per login/
/// checkout request) the breaker would rarely trip and the limiter would never bind, so a
/// degraded dependency could still pile up threads/connections indefinitely. This extension
/// re-configures the SAME resilience handler with thresholds matched to real dependency volume.
/// </para>
///
/// <para>
/// IMPORTANT: must be applied via <c>.AddStandardResilienceHandler(options => ...)</c> directly
/// on the named client's own <see cref="IHttpClientBuilder"/> — NOT via
/// <c>services.Configure&lt;HttpStandardResilienceOptions&gt;(clientName, ...)</c>, which was
/// verified (empirically, via a throwaway harness against a real flaky mock server) to have NO
/// effect on the pipeline actually built by <c>ConfigureHttpClientDefaults</c>.
/// </para>
/// </summary>
public static class ExternalDependencyResilience
{
    /// <summary>
    /// Applies a tuned resilience pipeline to a named external-dependency HttpClient: bounded
    /// concurrency (bulkhead), tight per-attempt/total timeouts, and a circuit breaker sized to
    /// trip within a handful of failures rather than requiring ~100 samples.
    /// </summary>
    /// <param name="builder">The named client's builder (from <c>services.AddHttpClient(name, ...)</c>).</param>
    /// <param name="attemptTimeout">Max time for a single attempt (before any retry).</param>
    /// <param name="totalRequestTimeout">Max time for the whole call including retries — the hard
    /// ceiling on how long a caller can be kept waiting on this dependency.</param>
    /// <param name="concurrencyLimit">Bulkhead: max concurrent in-flight calls to this dependency.
    /// Calls beyond this are rejected immediately (QueueLimit=0) rather than queued, so a slow
    /// dependency can never cause unbounded thread/connection growth.</param>
    /// <param name="circuitBreakerMinimumThroughput">Minimum sampled calls (within
    /// <paramref name="circuitBreakerSamplingDuration"/>) before the breaker will evaluate the
    /// failure ratio at all. Kept low here (vs. the library default of 100) to match our real
    /// per-dependency call volume.</param>
    /// <param name="retryAttempts">Retries beyond the first attempt. Kept low (default 1) —
    /// dependencies here are either idempotent-keyed (payment gateways) or cheap to retry once;
    /// aggressive retrying just prolongs the caller's wait during a real outage.</param>
    public static IHttpClientBuilder AddExternalDependencyResilience(
        this IHttpClientBuilder builder,
        TimeSpan attemptTimeout,
        TimeSpan totalRequestTimeout,
        int concurrencyLimit,
        int circuitBreakerMinimumThroughput,
        double circuitBreakerFailureRatio = 0.5,
        TimeSpan? circuitBreakerSamplingDuration = null,
        TimeSpan? circuitBreakerBreakDuration = null,
        int retryAttempts = 1)
    {
        builder.AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = retryAttempts;
            options.AttemptTimeout.Timeout = attemptTimeout;
            options.TotalRequestTimeout.Timeout = totalRequestTimeout;

            options.CircuitBreaker.SamplingDuration = circuitBreakerSamplingDuration ?? TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = circuitBreakerFailureRatio;
            options.CircuitBreaker.MinimumThroughput = circuitBreakerMinimumThroughput;
            options.CircuitBreaker.BreakDuration = circuitBreakerBreakDuration ?? TimeSpan.FromSeconds(20);

            // Bulkhead: bound concurrent calls to this one dependency. QueueLimit=0 — over the
            // limit fails immediately rather than queuing, which is what actually prevents pile-up.
            options.RateLimiter.DefaultRateLimiterOptions.PermitLimit = concurrencyLimit;
            options.RateLimiter.DefaultRateLimiterOptions.QueueLimit = 0;
        });

        return builder;
    }
}
