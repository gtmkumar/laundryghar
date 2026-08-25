using core.Application.Common.Interfaces;
using laundryghar.Utilities.Auth.ApiKey;

namespace core.Infrastructure.Auth;

/// <summary>
/// Bridges the API-key authentication handler (in Utilities) to the Argon2id hasher (here).
///
/// <para>Deliberately reuses <see cref="IPasswordHasher"/> rather than introducing a second hash
/// format for secrets. Two password-hashing implementations in one codebase means two things to keep
/// correct, and the second one is always the weaker — usually a bare SHA-256 someone reached for
/// because it was faster.</para>
/// </summary>
public sealed class ApiKeySecretVerifier : IApiKeySecretVerifier
{
    private readonly IPasswordHasher _hasher;

    public ApiKeySecretVerifier(IPasswordHasher hasher) => _hasher = hasher;

    public bool Verify(string secret, string hash)
    {
        try
        {
            return _hasher.Verify(secret, hash);
        }
        catch
        {
            // A malformed stored hash must read as "does not match", never as an exception that
            // becomes a 500 and tells the caller their guess was interesting.
            return false;
        }
    }
}
