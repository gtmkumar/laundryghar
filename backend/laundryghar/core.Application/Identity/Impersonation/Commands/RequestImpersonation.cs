using core.Application.Identity.Impersonation.Dtos;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.SharedDataModel.Contracts;
using laundryghar.SharedDataModel.Entities.IdentityAccess;
using laundryghar.Utilities.Auth.Audit;
using laundryghar.Utilities.Exceptions;

namespace core.Application.Identity.Impersonation.Commands;

/// <summary>Null result = unknown brand; the endpoint turns that into a 404.</summary>
public sealed record RequestImpersonationCommand(RequestImpersonationRequest Request, Guid SupportUserId)
    : ICommand<Guid?>;

/// <summary>
/// Support asks a provider for a session (§7). Creates a <c>pending</c> grant, which authorises
/// nothing on its own — it is a question, and the provider's approval is the answer.
///
/// <para>Goes through <see cref="IImpersonationStateStore.RequestAsync"/> rather than an EF insert:
/// the caller has no brand and no RLS bypass, so an ordinary INSERT could not pass the table's WITH
/// CHECK. See migration 0014 for why that is a one-capability function and not a bypass.</para>
/// </summary>
public sealed class RequestImpersonationCommandHandler : ICommandHandler<RequestImpersonationCommand, Guid?>
{
    private readonly IImpersonationStateStore _store;
    private readonly IAuditWriter _audit;

    public RequestImpersonationCommandHandler(IImpersonationStateStore store, IAuditWriter audit)
    {
        _store = store;
        _audit = audit;
    }

    public async Task<Guid?> HandleAsync(RequestImpersonationCommand cmd, CancellationToken ct)
    {
        var reason = cmd.Request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["reason"] = ["Say why you need access — the provider sees this when deciding."] });

        var scope = cmd.Request.Scope ?? ImpersonationScope.ReadOnly;
        if (scope is not (ImpersonationScope.ReadOnly or ImpersonationScope.ReadWrite))
            throw new ValidationException(new Dictionary<string, string[]>
            { ["scope"] = ["Scope must be read_only or read_write."] });

        var brandId = cmd.Request.BrandId;
        var supportUserId = cmd.SupportUserId;

        var grantId = await _store.RequestAsync(brandId, supportUserId, reason, scope, ct);
        if (grantId is null) return null;

        // Audited even though nothing was granted. "Who asked to get into my account, and when" is
        // information a provider is entitled to whether or not they said yes.
        await _audit.WriteAsync(
            action: "impersonation.requested",
            resourceType: "impersonation_grant",
            resourceId: grantId,
            resourceDisplay: reason,
            newValues: new { brandId, supportUserId, scope },
            ct: ct);

        return grantId;
    }
}
