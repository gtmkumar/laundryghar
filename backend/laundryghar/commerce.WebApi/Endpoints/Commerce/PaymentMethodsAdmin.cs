using commerce.Application.Commerce;
using commerce.Application.Commerce.Admin.PaymentMethods;
using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;

namespace commerce.WebApi.Endpoints.Commerce;

/// <summary>Admin — payment methods. All routes gated by permission:paymentmethod.manage.
/// Brand predicate applied in every handler: BrandId == ICurrentUser.RequireBrandId().</summary>
public class PaymentMethodsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/payment-methods";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Commerce - Payment Methods");
        group.RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:paymentmethod.manage");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:paymentmethod.manage");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreatePaymentMethodRequest>>()
            .RequireAuthorization("permission:paymentmethod.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:paymentmethod.manage");
        group.MapDelete(Delete, "/{id:guid}").RequireAuthorization("permission:paymentmethod.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        var r = await dispatcher.QueryAsync(new GetPaymentMethodsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
        return Results.Ok(new PaginatedListResponse<PaymentMethodDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetPaymentMethodByIdQuery(id), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreatePaymentMethodRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreatePaymentMethodCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/payment-methods/{r.Id}", new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdatePaymentMethodRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdatePaymentMethodCommand(id, req, u.UserId), ct);
        return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<PaymentMethodDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Delete(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var ok = await dispatcher.SendAsync(new DeletePaymentMethodCommand(id), ct);
        return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
    }
}
