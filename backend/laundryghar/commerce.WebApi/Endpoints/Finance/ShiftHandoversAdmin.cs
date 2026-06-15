using commerce.Application.Finance.CashBooks.Commands;
using commerce.Application.Finance.CashBooks.Dtos;
using commerce.Application.Finance.CashBooks.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Finance;

/// <summary>Admin — shift handovers. Reads gated by cashbook.read; create by cashbook.manage.</summary>
public class ShiftHandoversAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/shift-handovers";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Shift Handovers");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:cashbook.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateShiftHandoverRequest>>()
            .RequireAuthorization("permission:cashbook.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20,
        Guid? storeId = null, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetShiftHandoversQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, storeId, status), ct);
        return Results.Ok(new PaginatedListResponse<ShiftHandoverDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(
        CreateShiftHandoverRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateShiftHandoverCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/shift-handovers/{r.Id}",
            new SingleResponse<ShiftHandoverDto> { Status = true, Data = r });
    }
}
