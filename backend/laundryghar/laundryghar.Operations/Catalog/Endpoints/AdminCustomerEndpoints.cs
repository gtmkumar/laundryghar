using laundryghar.Catalog.Application.Customer.Admin.Commands;
using laundryghar.Catalog.Application.Customer.Admin.Dtos;
using laundryghar.Catalog.Application.Customer.Admin.Queries;
using MediatR;

namespace laundryghar.Catalog.Endpoints;

/// <summary>
/// Admin customer management endpoints.
/// Brand-scoped by RLS; platform admins can see across brands.
/// </summary>
public static class AdminCustomerEndpoints
{
    public static RouteGroupBuilder MapAdminCustomerEndpoints(this RouteGroupBuilder group)
    {
        var customers = group.MapGroup("/customers").WithTags("Admin - Customers");

        customers.MapPost("/", async (AdminCreateCustomerRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AdminCreateCustomerCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/customers/{r.Id}", new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:customer.create");

        customers.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null, string? search = null) =>
        {
            var r = await sender.Send(new GetCustomersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, search), ct);
            return Results.Ok(new PaginatedListResponse<AdminCustomerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:customer.read");

        customers.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetCustomerByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:customer.read");

        customers.MapPut("/{id:guid}", async (Guid id, AdminUpdateCustomerRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AdminUpdateCustomerCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:customer.update");

        customers.MapDelete("/{id:guid}", async (Guid id, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new AdminDeleteCustomerCommand(id, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:customer.delete");

        return group;
    }
}
