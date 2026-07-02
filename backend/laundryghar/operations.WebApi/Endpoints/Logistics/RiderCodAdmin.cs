using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using operations.Application.Logistics.Cod;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider COD reconciliation. Exposes the outstanding-COD view: cash riders
/// have collected in the field but not yet remitted/settled, grouped per rider with a
/// grand total. Gated on the rider-settlement permission. Thin dispatch through
/// <see cref="IDispatcher"/>; RLS scopes by brand.
/// </summary>
public class RiderCodAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/riders/cod";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider COD").RequireAuthorization();

        // Read-only reconciliation view → gate on the low-risk read permission (every role that
        // holds rider.settle also holds rider.read). Using rider.settle (high) would wrongly force
        // a §8 step-up on a dashboard READ; the settlement MUTATIONS keep rider.settle.
        group.MapGet(GetOutstanding, "/outstanding").RequireAuthorization("permission:rider.read");
    }

    /// <summary>GET /api/v1/admin/riders/cod/outstanding?riderId= — per-rider outstanding COD + grand total.</summary>
    public static async Task<IResult> GetOutstanding(
        ICurrentUser u, IDispatcher dispatcher, CancellationToken ct, Guid? riderId = null)
    {
        var r = await dispatcher.QueryAsync(new GetCodOutstandingQuery(u.RequireBrandId(), riderId), ct);
        return Results.Ok(new SingleResponse<CodOutstandingResponse> { Status = true, Data = r });
    }
}
