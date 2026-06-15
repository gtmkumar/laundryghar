using commerce.Application.Finance.Royalty.Commands;
using commerce.Application.Finance.Royalty.Dtos;
using commerce.Application.Finance.Royalty.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Finance;

/// <summary>Admin — royalty invoices. Reads gated by royalty.read; mutations by royalty.manage.</summary>
public class RoyaltyInvoicesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/royalty-invoices";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Royalty");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:royalty.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:royalty.read");
        group.MapPost(Generate, "/generate")
            .AddEndpointFilter<ValidationFilter<GenerateRoyaltyInvoiceRequest>>()
            .RequireAuthorization("permission:royalty.manage");
        group.MapPost(Issue, "/{id:guid}/issue").RequireAuthorization("permission:royalty.manage");
        group.MapPost(RecordPayment, "/{id:guid}/record-payment")
            .AddEndpointFilter<ValidationFilter<RecordRoyaltyPaymentRequest>>()
            .RequireAuthorization("permission:royalty.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20,
        Guid? franchiseId = null, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetRoyaltyInvoicesQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, franchiseId, status), ct);
        return Results.Ok(new PaginatedListResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetRoyaltyInvoiceByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Generate(
        GenerateRoyaltyInvoiceRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new GenerateRoyaltyInvoiceCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/royalty-invoices/{r.Id}",
            new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Issue(
        Guid id, IssueRoyaltyInvoiceRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new IssueRoyaltyInvoiceCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
    }

    public static async Task<IResult> RecordPayment(
        Guid id, RecordRoyaltyPaymentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new RecordRoyaltyPaymentCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
    }
}
