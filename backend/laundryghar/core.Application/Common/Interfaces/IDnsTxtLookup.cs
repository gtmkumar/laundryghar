namespace core.Application.Common.Interfaces;

/// <summary>
/// Reads DNS TXT records. The one external fact domain verification depends on: has the provider
/// published the challenge value we issued at the name we told them to publish it at?
///
/// <para>An interface rather than a direct DNS call so the verification handler is testable without
/// touching the network — the tests supply success / wrong-value / missing-record / lookup-failure
/// resolvers and assert what each one does to <c>verified_at</c>.</para>
/// </summary>
public interface IDnsTxtLookup
{
    /// <summary>
    /// Every TXT string published at <paramref name="name"/>. Returns an EMPTY list when the name
    /// does not exist or has no TXT records — "no records" is a normal, expected answer during
    /// verification (the provider has not added it yet), not an error.
    /// </summary>
    /// <exception cref="DnsLookupException">
    /// The lookup itself could not be performed (timeout, resolver unreachable, SERVFAIL). This is
    /// distinct from "the name has no TXT records" and MUST NOT be reported to the provider as
    /// "verification failed" — nothing was learned about their DNS.
    /// </exception>
    Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken ct = default);
}

/// <summary>The lookup could not be completed, so nothing is known about the name. Distinct from a
/// successful lookup that found no matching record.</summary>
public sealed class DnsLookupException : Exception
{
    public DnsLookupException(string message, Exception? inner = null) : base(message, inner) { }
}
