using core.Application.Common.Interfaces;
using DnsClient;
using Microsoft.Extensions.Logging;

namespace core.Infrastructure.Dns;

/// <summary>
/// <see cref="IDnsTxtLookup"/> over DnsClient.NET. Used only by domain verification, which runs once
/// per provider click — not a hot path, so correctness and clear failure reporting matter more than
/// throughput.
///
/// <para><b>Queries are deliberately NOT cached and NOT taken from the OS resolver cache.</b>
/// Verification is the one moment where a stale negative answer is maximally annoying: the provider
/// has just added the record and is clicking "Verify". <see cref="LookupClient"/> is configured with
/// caching off so each attempt is a real query.</para>
/// </summary>
public sealed class DnsTxtLookup : IDnsTxtLookup
{
    private readonly ILookupClient _client;
    private readonly ILogger<DnsTxtLookup> _logger;

    public DnsTxtLookup(ILogger<DnsTxtLookup> logger)
        : this(new LookupClient(new LookupClientOptions
        {
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(5),
            Retries = 2,
            // A provider mid-setup routinely has no record yet; that is an empty answer, not a fault
            // to throw on. We surface "not found" as an empty list from the interface instead.
            ThrowDnsErrors = false,
        }), logger)
    { }

    /// <summary>Test seam: inject a client.</summary>
    public DnsTxtLookup(ILookupClient client, ILogger<DnsTxtLookup> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: ct);

            // A resolver-level failure (SERVFAIL, refused, unreachable) is NOT "no records" — the
            // difference decides whether the provider is told "not found yet" or "try again".
            // NXDOMAIN is excluded: "this name does not exist" IS a definitive empty answer.
            if (result.HasError && result.Header.ResponseCode != DnsHeaderResponseCode.NotExistentDomain)
                throw new DnsLookupException(result.ErrorMessage);

            return result.Answers.TxtRecords()
                // A TXT record is a sequence of ≤255-byte strings that the zone may have split; the
                // published value is their concatenation, which is how every resolver reassembles it.
                .Select(r => string.Concat(r.Text))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        catch (DnsLookupException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled — not a DNS fault
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS TXT lookup failed for {Name}.", name);
            throw new DnsLookupException($"DNS lookup failed for '{name}'.", ex);
        }
    }
}
