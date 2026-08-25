using System.Security.Cryptography;
using core.Application.Common.Interfaces;
using core.Application.Identity.ApiKeys.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth.ApiKey;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace core.Application.Identity.ApiKeys.Commands;

public sealed record CreateApiKeyCommand(Guid BrandId, CreateApiKeyRequest Request, Guid? ActorId)
    : ICommand<CreatedApiKeyDto>;

/// <summary>
/// Issues a machine credential (§11 P4). The secret is generated here, returned once, and stored
/// only as an Argon2id hash.
///
/// <para><b>Why the secret is never recoverable.</b> The alternative — encrypting it so it can be
/// shown again — means one key compromise makes every provider's credentials readable. "You will
/// have to issue a new key" is a small inconvenience; the other failure mode is a platform-wide
/// breach. Stripe, GitHub and AWS all made the same call.</para>
/// </summary>
public sealed class CreateApiKeyCommandHandler : ICommandHandler<CreateApiKeyCommand, CreatedApiKeyDto>
{
    /// <summary>Bytes of entropy in the secret. 32 is 256 bits — well past anything brute-forceable,
    /// and the cost of more is only a longer string in someone's config file.</summary>
    private const int SecretBytes = 32;

    /// <summary>Bytes in the public handle. 8 bytes is exactly <see cref="ApiKeyClaims.HandleLength"/>
    /// hex characters — enough that collisions are not a practical concern, short enough to read out
    /// over a call, and a FIXED length, which is what lets the parser tell the handle from a secret
    /// that contains underscores.</summary>
    private const int PrefixBytes = ApiKeyClaims.HandleLength / 2;

    /// <summary>How many live keys one brand may hold. Not a licensing limit — a blast-radius one.
    /// A brand with hundreds of keys cannot audit them, and cannot answer "which of these leaked".</summary>
    public const int MaxActiveKeysPerBrand = 25;

    private readonly ICoreDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;

    public CreateApiKeyCommandHandler(ICoreDbContext db, IPasswordHasher hasher, IAuditWriter audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    public async Task<CreatedApiKeyDto> HandleAsync(CreateApiKeyCommand cmd, CancellationToken ct)
    {
        var name = cmd.Request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["name"] = ["Name this key after the integration that will use it."] });

        var environment = cmd.Request.Environment ?? ApiKeyEnvironment.Live;
        if (environment is not (ApiKeyEnvironment.Live or ApiKeyEnvironment.Test))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["environment"] = ["Environment must be live or test."] });

        if (cmd.Request.ExpiresAt is { } exp && exp <= DateTimeOffset.UtcNow)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["expiresAt"] = ["An expiry in the past would issue a key that never works."] });

        var active = await _db.ApiKeys.CountAsync(
            k => k.BrandId == cmd.BrandId && k.Status == ApiKeyStatus.Active, ct);
        if (active >= MaxActiveKeysPerBrand)
            throw new ValidationException(new Dictionary<string, string[]>
            { ["name"] = [$"This account already has {MaxActiveKeysPerBrand} active keys. Revoke one first."] });

        // Scopes are stored exactly as given, de-duplicated. No normalisation, no expansion: a scope
        // the caller did not ask for must never appear on a key, and an unrecognised one simply
        // matches nothing rather than being silently dropped (which would look like it was granted).
        var scopes = (cmd.Request.Scopes ?? [])
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var handle = Convert.ToHexString(RandomNumberGenerator.GetBytes(PrefixBytes)).ToLowerInvariant();
        var prefix = $"lg_{environment}_{handle}";
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

        var now = DateTimeOffset.UtcNow;
        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            BrandId = cmd.BrandId,
            Name = name,
            KeyPrefix = prefix,
            SecretHash = _hasher.Hash(secret),
            Environment = environment,
            Scopes = scopes,
            Status = ApiKeyStatus.Active,
            RateLimitPerMinute = cmd.Request.RateLimitPerMinute,
            ExpiresAt = cmd.Request.ExpiresAt,
            CreatedAt = now, UpdatedAt = now, CreatedBy = cmd.ActorId, UpdatedBy = cmd.ActorId,
        };
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync(ct);

        // Audited with the PREFIX and the scopes, never the secret. "Who minted a credential to this
        // business, when, and how much could it do" is precisely what an incident review asks.
        await _audit.WriteAsync("api_key.created", "api_key", key.Id, prefix,
            newValues: new { name, environment, scopes, expiresAt = key.ExpiresAt }, ct: ct);

        return new CreatedApiKeyDto(key.Id, name, $"{prefix}_{secret}", prefix, scopes, environment, key.ExpiresAt);
    }

    /// <summary>Base64url without padding — safe in a header, a URL, and a shell command, which is
    /// where these end up.</summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
