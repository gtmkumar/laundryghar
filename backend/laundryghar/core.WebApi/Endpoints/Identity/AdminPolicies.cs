using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Authorization.Abac;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;

namespace core.WebApi.Endpoints.Identity;

/// <summary>
/// Admin — ABAC policies and the decision explain view (/api/v1/admin/policies), A8.
///
/// <para>This is the screen that replaces the permission checkbox matrix as the place rules are
/// read. The matrix answers "does this role hold this code"; it cannot express — or show — a rule
/// like "refunds above ₹5,000 need a manager", because there is nowhere in a checkbox to put the
/// ₹5,000. A policy row can, so the console has to be able to show one.</para>
///
/// <para>Reads are gated on <c>roles.list</c> and writes on <c>permissions.assign</c>: a policy edit
/// is a change to who can do what, so it belongs behind the same critical permission that assigning
/// a permission does, not behind a new weaker one.</para>
/// </summary>
public class AdminPolicies : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/policies";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Policies").RequireAuthorization();

        group.MapGet(ListPolicies).RequireAuthorization("permission:roles.list");
        group.MapGet(ListDecisions, "decisions").RequireAuthorization("permission:audit.read");
        group.MapPatch(UpdatePolicy, "{id:guid}").RequireAuthorization("permission:permissions.assign");
    }

    public static async Task<IResult> ListPolicies(
        IPolicyAdminService policies, ICurrentUser user, CancellationToken ct,
        string? resourceType = null, string? search = null)
    {
        // A platform admin sees every brand's policies; anyone else sees their own brand's plus the
        // platform-authored ones that govern them.
        var brandScope = user.IsPlatformAdmin ? null : user.TryGetBrandId();

        var data = await policies.ListAsync(brandScope, resourceType, search, ct);
        return Results.Ok(new ListResponse<PolicyListItem> { Status = true, Data = data.ToList() });
    }

    /// <summary>
    /// A8.2 — "why was this denied". Reads authz.decision_log, which records every decision, permit
    /// and deny, read and write. That is strictly more than the existing audit trail can offer:
    /// AuditSaveChangesInterceptor fires only on Added/Modified/Deleted, so a denied READ leaves no
    /// record anywhere else in the system.
    /// </summary>
    public static async Task<IResult> ListDecisions(
        IPolicyAdminService policies, ICurrentUser user, CancellationToken ct,
        Guid? userId = null, bool deniesOnly = true, int limit = 100)
    {
        var brandScope = user.IsPlatformAdmin ? null : user.TryGetBrandId();

        var data = await policies.RecentDecisionsAsync(brandScope, userId, deniesOnly, limit, ct);
        return Results.Ok(new ListResponse<DecisionLogItem> { Status = true, Data = data.ToList() });
    }

    public static async Task<IResult> UpdatePolicy(
        Guid id, PolicyEdit edit, IPolicyAdminService policies, ICurrentUser user, CancellationToken ct)
    {
        // Null brand scope = platform admin, who may edit anything. A brand admin passes their own
        // brand and the guardrail in the UPDATE refuses a loosening edit to a platform policy.
        var actorBrand = user.IsPlatformAdmin ? null : user.TryGetBrandId();

        var updated = await policies.UpdateAsync(id, actorBrand, user.UserId, edit, ct);

        return updated
            ? Results.Ok(new Response { Status = true })
            : Results.NotFound(new Response
            {
                Status = false,
                Message = new Message
                {
                    ErrorTypeCode = ErrorMessageEnum.NotFound,
                    ResponseMessage = "policy_not_editable",
                    ErrorMessage = new Dictionary<string, string[]>
                    {
                        ["policy_not_editable"] =
                        [
                            "No such policy, or it is platform-authored and this edit would loosen it. " +
                            "A brand may tighten a platform policy but never relax one.",
                        ],
                    },
                },
            });
    }
}
