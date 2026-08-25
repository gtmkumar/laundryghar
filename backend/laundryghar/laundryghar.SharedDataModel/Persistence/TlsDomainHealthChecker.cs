using System.Net.Security;
using System.Security.Authentication;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using laundryghar.SharedDataModel.Contracts;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// <see cref="IDomainHealthChecker"/> that opens a real TLS connection and reads the certificate the
/// domain actually serves.
///
/// <para>A real handshake rather than an HTTP request, for two reasons. It answers the question
/// directly — the certificate's own expiry and subject, not an inference from a status code — and it
/// works for a domain whose application is down but whose TLS is fine, which is a materially
/// different problem from an expiring certificate.</para>
///
/// <para>Certificate validation is accepted at the transport level and judged HERE instead. That
/// looks alarming and is the point: rejecting an expired certificate at the handshake would throw,
/// and we would learn only "it failed" rather than "it expired on Tuesday" — which is the one fact
/// worth reporting. Nothing is sent over this connection and no data is read from it.</para>
/// </summary>
public sealed class TlsDomainHealthChecker : IDomainHealthChecker
{
    /// <summary>A certificate closer than this to expiry is reported as degraded — early enough that
    /// a 90-day certificate's normal renewal window has already come and gone unaddressed.</summary>
    public static readonly TimeSpan ExpiryWarning = TimeSpan.FromDays(14);

    /// <summary>Per-check ceiling. A sweep over many domains must not be held up by one that
    /// blackholes packets.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<DomainHealthResult> CheckAsync(string domain, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        X509Certificate2? certificate = null;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(domain, 443, timeout.Token);

            await using var tls = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (_, cert, _, _) =>
                {
                    // Captured, not judged. See the class comment: throwing here would lose the
                    // reason, and the reason is the entire value of the check.
                    if (cert is not null) certificate = new X509Certificate2(cert);
                    return true;
                });

            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = domain,
            }, timeout.Token);
        }
        catch (Exception ex)
        {
            return new DomainHealthResult(DomainHealth.Unreachable, Describe(ex), null, null);
        }

        if (certificate is null)
            return new DomainHealthResult(
                DomainHealth.Degraded, "Connected, but the server presented no certificate.", null, null);

        using (certificate)
        {
            var expiry = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            var issuer = Shorten(certificate.Issuer);
            var now = DateTimeOffset.UtcNow;

            if (expiry <= now)
                return new DomainHealthResult(DomainHealth.Degraded,
                    $"The certificate expired on {expiry:yyyy-MM-dd}.", expiry, issuer);

            if (expiry - now <= ExpiryWarning)
                return new DomainHealthResult(DomainHealth.Degraded,
                    $"The certificate expires on {expiry:yyyy-MM-dd}.", expiry, issuer);

            if (!MatchesHost(certificate, domain))
                return new DomainHealthResult(DomainHealth.Degraded,
                    "The certificate is valid but issued for a different host.", expiry, issuer);

            return new DomainHealthResult(DomainHealth.Healthy, null, expiry, issuer);
        }
    }

    /// <summary>Does the certificate actually cover this host? A valid certificate for the WRONG name
    /// still produces a browser warning, so "valid" alone is not the question.</summary>
    private static bool MatchesHost(X509Certificate2 certificate, string domain)
    {
        // MatchesHostname handles wildcards and Subject Alternative Names, which a naive CN string
        // comparison gets wrong for exactly the certificates a multi-tenant platform uses most.
        try
        {
            return certificate.MatchesHostname(domain, allowWildcards: true, allowCommonName: true);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The issuer's CN, not the whole distinguished name — "R11" reads better in a console
    /// than forty characters of X.500.</summary>
    private static string Shorten(string distinguishedName)
    {
        foreach (var part in distinguishedName.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
                return trimmed[3..];
        }
        return distinguishedName.Length > 200 ? distinguishedName[..200] : distinguishedName;
    }

    private static string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => $"No response within {Timeout.TotalSeconds:0}s.",
        SocketException s          => $"Could not connect: {s.SocketErrorCode}.",
        AuthenticationException a  => $"TLS handshake failed: {a.Message}",
        _                          => ex.Message.Length > 500 ? ex.Message[..500] : ex.Message,
    };
}
