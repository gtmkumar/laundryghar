using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Catalog.Customer.Admin.Commands;
using operations.Application.Catalog.Customer.Admin.Dtos;
using operations.Application.Catalog.Customer.Admin.Queries;

namespace operations.WebApi.Endpoints.Catalog;

/// <summary>
/// Admin — customer management (counter/admin lane). Brand-scoped in handlers; platform admins
/// can see across brands. Per-route permission policies.
/// </summary>
public class CustomersAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/customers";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Customers");

        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<AdminCreateCustomerRequest>>()
            .RequireAuthorization("permission:customer.create");
        group.MapGet(GetAll, "/").RequireAuthorization("permission:customer.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:customer.read");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:customer.update");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:customer.delete");
    }

    public static async Task<IResult> Create(AdminCreateCustomerRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AdminCreateCustomerCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/customers/{r.Id}",
            new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, string? status = null, string? search = null)
    {
        var r = await dispatcher.QueryAsync(new GetCustomersQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, search), ct);
        return Results.Ok(new PaginatedListResponse<AdminCustomerDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetCustomerByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, AdminUpdateCustomerRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new AdminUpdateCustomerCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<AdminCustomerDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new AdminDeleteCustomerCommand(id, u.UserId), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
