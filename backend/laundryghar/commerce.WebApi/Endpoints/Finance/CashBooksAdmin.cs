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

/// <summary>Admin — cash books. Reads gated by cashbook.read; mutations by cashbook.manage.</summary>
public class CashBooksAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/cash-books";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Cash Books");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:cashbook.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:cashbook.read");
        group.MapPost(Open, "/")
            .AddEndpointFilter<ValidationFilter<OpenCashBookRequest>>()
            .RequireAuthorization("permission:cashbook.manage");
        group.MapPost(AddEntry, "/{id:guid}/entries")
            .AddEndpointFilter<ValidationFilter<AddCashBookEntryRequest>>()
            .RequireAuthorization("permission:cashbook.manage");
        group.MapPost(Close, "/{id:guid}/close")
            .AddEndpointFilter<ValidationFilter<CloseCashBookRequest>>()
            .RequireAuthorization("permission:cashbook.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20,
        Guid? storeId = null, string? status = null, DateOnly? bookDate = null)
    {
        var r = await dispatcher.QueryAsync(new GetCashBooksQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, storeId, status, bookDate), ct);
        return Results.Ok(new PaginatedListResponse<CashBookSummaryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetCashBookByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Open(
        OpenCashBookRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new OpenCashBookCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/cash-books/{r.Id}",
            new SingleResponse<CashBookDto> { Status = true, Data = r });
    }

    public static async Task<IResult> AddEntry(
        Guid id, AddCashBookEntryRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AddCashBookEntryCommand(id, req, u.UserId), ct);
        return Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Close(
        Guid id, CloseCashBookRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CloseCashBookCommand(id, req, u.UserId), ct);
        return Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
    }
}
