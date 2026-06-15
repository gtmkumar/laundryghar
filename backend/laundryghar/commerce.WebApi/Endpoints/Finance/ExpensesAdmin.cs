using commerce.Application.Finance.Expenses.Commands;
using commerce.Application.Finance.Expenses.Dtos;
using commerce.Application.Finance.Expenses.Queries;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Common;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Finance;

/// <summary>Admin — expenses. Reads gated by expense.read; create/edit by expense.manage;
/// approve/reject by expense.approve.</summary>
public class ExpensesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/expenses";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Expenses");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:expense.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:expense.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateExpenseRequest>>()
            .RequireAuthorization("permission:expense.manage");
        group.MapPost(Approve, "/{id:guid}/approve").RequireAuthorization("permission:expense.approve");
        group.MapPost(Reject, "/{id:guid}/reject")
            .AddEndpointFilter<ValidationFilter<RejectExpenseRequest>>()
            .RequireAuthorization("permission:expense.approve");
        group.MapPost(MarkPaid, "/{id:guid}/mark-paid").RequireAuthorization("permission:expense.manage");
        group.MapPost(AddAttachment, "/{id:guid}/attachments")
            .AddEndpointFilter<ValidationFilter<AddExpenseAttachmentRequest>>()
            .RequireAuthorization("permission:expense.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20,
        string? status = null, Guid? categoryId = null, Guid? storeId = null)
    {
        var r = await dispatcher.QueryAsync(new GetExpensesQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, categoryId, storeId), ct);
        return Results.Ok(new PaginatedListResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetExpenseByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(
        CreateExpenseRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateExpenseCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/expenses/{r.Id}",
            new SingleResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Approve(
        Guid id, ApproveExpenseRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new ApproveExpenseCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Reject(
        Guid id, RejectExpenseRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new RejectExpenseCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> MarkPaid(
        Guid id, MarkExpensePaidRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new MarkExpensePaidCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
    }

    public static async Task<IResult> AddAttachment(
        Guid id, AddExpenseAttachmentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AddExpenseAttachmentCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Created($"/api/v1/admin/expenses/{id}/attachments/{r.Id}",
                new SingleResponse<ExpenseAttachmentDto> { Status = true, Data = r });
    }
}
