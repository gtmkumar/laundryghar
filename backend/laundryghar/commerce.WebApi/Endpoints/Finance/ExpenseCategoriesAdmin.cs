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

/// <summary>Admin — expense categories (lookup). Reads gated by expense.read; mutations by expense.manage.</summary>
public class ExpenseCategoriesAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/expense-categories";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Expense Categories");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:expense.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:expense.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateExpenseCategoryRequest>>()
            .RequireAuthorization("permission:expense.manage");
        group.MapPut(Update, "/{id:guid}")
            .AddEndpointFilter<ValidationFilter<UpdateExpenseCategoryRequest>>()
            .RequireAuthorization("permission:expense.manage");
    }

    public static async Task<IResult> GetAll(
        IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null)
    {
        var r = await dispatcher.QueryAsync(new GetExpenseCategoriesQuery(
            page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
        return Results.Ok(new PaginatedListResponse<ExpenseCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetExpenseCategoryByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(
        CreateExpenseCategoryRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateExpenseCategoryCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/expense-categories/{r.Id}",
            new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(
        Guid id, UpdateExpenseCategoryRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateExpenseCategoryCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
    }
}
