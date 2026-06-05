using laundryghar.Finance.Application.CashBooks.Commands;
using laundryghar.Finance.Application.CashBooks.Dtos;
using laundryghar.Finance.Application.CashBooks.Queries;
using laundryghar.Finance.Application.Expenses.Commands;
using laundryghar.Finance.Application.Expenses.Dtos;
using laundryghar.Finance.Application.Expenses.Queries;
using laundryghar.Finance.Application.Royalty.Commands;
using laundryghar.Finance.Application.Royalty.Dtos;
using laundryghar.Finance.Application.Royalty.Queries;
using MediatR;

namespace laundryghar.Finance.Endpoints;

public static class FinanceEndpoints
{
    public static WebApplication MapFinanceEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization();

        // ── Expense Categories (lookup) ────────────────────────────────────────
        var cats = admin.MapGroup("/expense-categories").WithTags("Admin - Expense Categories");

        cats.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var r = await sender.Send(new GetExpenseCategoriesQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<ExpenseCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.read");

        cats.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetExpenseCategoryByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.read");

        cats.MapPost("/", async (CreateExpenseCategoryRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateExpenseCategoryCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/expense-categories/{r.Id}",
                new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.manage");

        cats.MapPut("/{id:guid}", async (Guid id, UpdateExpenseCategoryRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateExpenseCategoryCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseCategoryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.manage");

        // ── Expenses ──────────────────────────────────────────────────────────
        var expenses = admin.MapGroup("/expenses").WithTags("Admin - Expenses");

        expenses.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            string? status = null, Guid? categoryId = null, Guid? storeId = null) =>
        {
            var r = await sender.Send(new GetExpensesQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, categoryId, storeId), ct);
            return Results.Ok(new PaginatedListResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.read");

        expenses.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetExpenseByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.read");

        expenses.MapPost("/", async (CreateExpenseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateExpenseCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/expenses/{r.Id}",
                new SingleResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.manage");

        expenses.MapPost("/{id:guid}/approve", async (Guid id, ApproveExpenseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new ApproveExpenseCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.approve");

        expenses.MapPost("/{id:guid}/reject", async (Guid id, RejectExpenseRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new RejectExpenseCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.approve");

        expenses.MapPost("/{id:guid}/mark-paid", async (Guid id, MarkExpensePaidRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new MarkExpensePaidCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<ExpenseDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.manage");

        expenses.MapPost("/{id:guid}/attachments", async (Guid id, AddExpenseAttachmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AddExpenseAttachmentCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/admin/expenses/{id}/attachments/{r.Id}",
                    new SingleResponse<ExpenseAttachmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:expense.manage");

        // ── Cash Books ────────────────────────────────────────────────────────
        var books = admin.MapGroup("/cash-books").WithTags("Admin - Cash Books");

        books.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            Guid? storeId = null, string? status = null, DateOnly? bookDate = null) =>
        {
            var r = await sender.Send(new GetCashBooksQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, storeId, status, bookDate), ct);
            return Results.Ok(new PaginatedListResponse<CashBookSummaryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.read");

        books.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetCashBookByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.read");

        books.MapPost("/", async (OpenCashBookRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new OpenCashBookCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/cash-books/{r.Id}",
                new SingleResponse<CashBookDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.manage");

        books.MapPost("/{id:guid}/entries", async (Guid id, AddCashBookEntryRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AddCashBookEntryCommand(id, req, u.UserId), ct);
            return Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.manage");

        books.MapPost("/{id:guid}/close", async (Guid id, CloseCashBookRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CloseCashBookCommand(id, req, u.UserId), ct);
            return Results.Ok(new SingleResponse<CashBookDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.manage");

        // ── Shift Handovers ───────────────────────────────────────────────────
        var handovers = admin.MapGroup("/shift-handovers").WithTags("Admin - Shift Handovers");

        handovers.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            Guid? storeId = null, string? status = null) =>
        {
            var r = await sender.Send(new GetShiftHandoversQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, storeId, status), ct);
            return Results.Ok(new PaginatedListResponse<ShiftHandoverDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.read");

        handovers.MapPost("/", async (CreateShiftHandoverRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateShiftHandoverCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/shift-handovers/{r.Id}",
                new SingleResponse<ShiftHandoverDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:cashbook.manage");

        // ── Royalty Invoices ──────────────────────────────────────────────────
        var royalty = admin.MapGroup("/royalty-invoices").WithTags("Admin - Royalty");

        royalty.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20,
            Guid? franchiseId = null, string? status = null) =>
        {
            var r = await sender.Send(new GetRoyaltyInvoicesQuery(
                page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, franchiseId, status), ct);
            return Results.Ok(new PaginatedListResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:royalty.read");

        royalty.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetRoyaltyInvoiceByIdQuery(id), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:royalty.read");

        royalty.MapPost("/generate", async (GenerateRoyaltyInvoiceRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GenerateRoyaltyInvoiceCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/royalty-invoices/{r.Id}",
                new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:royalty.manage");

        royalty.MapPost("/{id:guid}/issue", async (Guid id, IssueRoyaltyInvoiceRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new IssueRoyaltyInvoiceCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:royalty.manage");

        royalty.MapPost("/{id:guid}/record-payment", async (Guid id, RecordRoyaltyPaymentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new RecordRoyaltyPaymentCommand(id, req, u.UserId), ct);
            return r is null
                ? Results.NotFound()
                : Results.Ok(new SingleResponse<RoyaltyInvoiceDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:royalty.manage");

        return app;
    }
}
