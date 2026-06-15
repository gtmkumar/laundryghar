using LaundryGhar.Utilities.CQRS.Abstractions;
using laundryghar.Utilities.ApiResponse.ResponseUtil;
using laundryghar.Utilities.Endpoints;
using laundryghar.Utilities.Services;
using laundryghar.Utilities.Validation;
using operations.Application.Logistics.Assignments.Commands.CreateRiderAssignment;
using operations.Application.Logistics.Assignments.Commands.UpdateRiderAssignment;
using operations.Application.Logistics.Assignments.Dtos;
using operations.Application.Logistics.Assignments.Queries.GetAssignmentById;
using operations.Application.Logistics.Assignments.Queries.GetAssignments;

namespace operations.WebApi.Endpoints.Logistics;

/// <summary>
/// Admin — Rider shift assignments: paged list, by-id, create, and update. Thin dispatch
/// through <see cref="IDispatcher"/>.
/// </summary>
public class RiderAssignmentsAdmin : IEndpointGroup
{
    public static string? RoutePrefix => "/api/v1/admin/rider-assignments";

    public static void Map(RouteGroupBuilder group)
    {
        group.WithTags("Admin - Rider Assignments").RequireAuthorization();

        group.MapGet(GetAll, "/").RequireAuthorization("permission:rider.assignment.read");
        group.MapGet(GetById, "/{id:guid}").RequireAuthorization("permission:rider.assignment.read");
        group.MapPost(Create, "/")
            .AddEndpointFilter<ValidationFilter<CreateRiderAssignmentRequest>>()
            .RequireAuthorization("permission:rider.assignment.manage");
        group.MapPut(Update, "/{id:guid}").RequireAuthorization("permission:rider.assignment.manage");
    }

    public static async Task<IResult> GetAll(IDispatcher dispatcher, CancellationToken ct,
        int page = 1, int pageSize = 20, Guid? riderId = null, string? status = null, DateOnly? shiftDate = null)
    {
        var r = await dispatcher.QueryAsync(
            new GetAssignmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, riderId, status, shiftDate), ct);
        return Results.Ok(new PaginatedListResponse<RiderAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> GetById(Guid id, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.QueryAsync(new GetAssignmentByIdQuery(id), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Create(CreateRiderAssignmentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new CreateRiderAssignmentCommand(req, u.UserId), ct);
        return Results.Created($"/api/v1/admin/rider-assignments/{r.Id}",
            new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
    }

    public static async Task<IResult> Update(Guid id, UpdateRiderAssignmentRequest req, ICurrentUser u, IDispatcher dispatcher, CancellationToken ct)
    {
        var r = await dispatcher.SendAsync(new UpdateRiderAssignmentCommand(id, req, u.UserId), ct);
        return r is null
            ? Results.NotFound()
            : Results.Ok(new SingleResponse<RiderAssignmentDto> { Status = true, Data = r });
    }
}
