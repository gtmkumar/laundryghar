using laundryghar.Warehouse.Application.Board.Dtos;
using laundryghar.Warehouse.Application.Board.Queries;
using laundryghar.Warehouse.Application.Batches.Commands;
using laundryghar.Warehouse.Application.Batches.Dtos;
using laundryghar.Warehouse.Application.Batches.Queries;
using laundryghar.Warehouse.Application.Garments.Commands;
using laundryghar.Warehouse.Application.Garments.Dtos;
using laundryghar.Warehouse.Application.Garments.Queries;
using laundryghar.Warehouse.Application.Inspections.Commands;
using laundryghar.Warehouse.Application.Inspections.Dtos;
using laundryghar.Warehouse.Application.Inspections.Queries;
using laundryghar.Warehouse.Application.Processes.Commands;
using laundryghar.Warehouse.Application.Processes.Dtos;
using laundryghar.Warehouse.Application.Processes.Queries;
using laundryghar.Warehouse.Application.QualityChecks.Commands;
using laundryghar.Warehouse.Application.QualityChecks.Dtos;
using laundryghar.Warehouse.Application.QualityChecks.Queries;
using laundryghar.Warehouse.Application.StockReconciliation.Commands;
using laundryghar.Warehouse.Application.StockReconciliation.Dtos;
using laundryghar.Warehouse.Application.StockReconciliation.Queries;
using MediatR;

namespace laundryghar.Warehouse.Endpoints;

public static class WarehouseEndpoints
{
    public static WebApplication MapWarehouseEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization();

        // ── Garments ──────────────────────────────────────────────────────────
        var garments = admin.MapGroup("/garments").WithTags("Admin - Garments");

        // Warehouse kanban read model (per-stage cards + header metrics).
        garments.MapGet("/board", async (ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetWarehouseBoardQuery(), ct);
            return Results.Ok(new SingleResponse<WarehouseBoardDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.read");

        garments.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? stage = null, Guid? storeId = null, Guid? batchId = null) =>
        {
            var r = await sender.Send(new GetGarmentsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, stage, storeId, batchId), ct);
            return Results.Ok(new PaginatedListResponse<GarmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.read");

        garments.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetGarmentByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<GarmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.read");

        garments.MapGet("/by-tag/{tagCode}", async (string tagCode, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetGarmentByTagQuery(tagCode), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<GarmentJourneyDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.read");

        garments.MapPost("/", async (CreateGarmentRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateGarmentCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/garments/{r.Id}",
                new SingleResponse<GarmentDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.tag");

        // ── Garment Tags ──────────────────────────────────────────────────────
        var tags = admin.MapGroup("/garment-tags").WithTags("Admin - Garment Tags");

        tags.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 50, string? status = null) =>
        {
            var r = await sender.Send(new GetTagsQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<GarmentTagDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.tag");

        tags.MapPost("/generate", async (GenerateTagsRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GenerateTagsCommand(req, u.UserId), ct);
            return Results.Ok(new ListResponse<GarmentTagDto> { Status = true, Data = r.ToList() });
        }).RequireAuthorization("permission:garment.tag");

        // ── Inspections ───────────────────────────────────────────────────────
        var inspections = admin.MapGroup("/garment-inspections").WithTags("Admin - Inspections");

        inspections.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            Guid? garmentId = null, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetGarmentInspectionsQuery(garmentId ?? Guid.Empty, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<GarmentInspectionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.inspect");

        inspections.MapPost("/", async (CreateInspectionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateInspectionCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/garment-inspections/{r.Id}",
                new SingleResponse<GarmentInspectionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.inspect");

        // ── Garment Conditions (lookup) ───────────────────────────────────────
        var conditions = admin.MapGroup("/garment-conditions").WithTags("Admin - Garment Conditions");

        conditions.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 50) =>
        {
            var r = await sender.Send(new GetGarmentConditionsQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<GarmentConditionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.inspect");

        conditions.MapPost("/", async (CreateGarmentConditionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateGarmentConditionCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/garment-conditions/{r.Id}",
                new SingleResponse<GarmentConditionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.inspect");

        conditions.MapPut("/{id:guid}", async (Guid id, UpdateGarmentConditionRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateGarmentConditionCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<GarmentConditionDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:garment.inspect");

        // ── Warehouse Batches ─────────────────────────────────────────────────
        var batches = admin.MapGroup("/warehouse-batches").WithTags("Admin - Warehouse Batches");

        batches.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null, Guid? warehouseId = null) =>
        {
            var r = await sender.Send(new GetBatchesQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status, warehouseId), ct);
            return Results.Ok(new PaginatedListResponse<WarehouseBatchDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.batch.manage");

        batches.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetBatchByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WarehouseBatchDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.batch.manage");

        batches.MapPost("/", async (CreateWarehouseBatchRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateWarehouseBatchCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/warehouse-batches/{r.Id}",
                new SingleResponse<WarehouseBatchDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.batch.manage");

        batches.MapPut("/{id:guid}", async (Guid id, UpdateWarehouseBatchRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new UpdateWarehouseBatchCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<WarehouseBatchDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.batch.manage");

        batches.MapPost("/{id:guid}/garments/{garmentId:guid}", async (Guid id, Guid garmentId, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new AddGarmentToBatchCommand(id, garmentId, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:warehouse.batch.manage");

        batches.MapDelete("/{id:guid}/garments/{garmentId:guid}", async (Guid id, Guid garmentId, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var ok = await sender.Send(new RemoveGarmentFromBatchCommand(id, garmentId, u.UserId), ct);
            return ok ? Results.Ok(new Response { Status = true }) : Results.NotFound();
        }).RequireAuthorization("permission:warehouse.batch.manage");

        // ── Warehouse Processes (lookup) ──────────────────────────────────────
        var procs = admin.MapGroup("/warehouse-processes").WithTags("Admin - Warehouse Processes");

        procs.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 50) =>
        {
            var r = await sender.Send(new GetWarehouseProcessesQuery(page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<WarehouseProcessDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.process.scan");

        procs.MapPost("/", async (CreateWarehouseProcessRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateWarehouseProcessCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/warehouse-processes/{r.Id}",
                new SingleResponse<WarehouseProcessDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.process.scan");

        // ── Process Logs ──────────────────────────────────────────────────────
        var logs = admin.MapGroup("/process-logs").WithTags("Admin - Process Logs");

        logs.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            Guid? garmentId = null, Guid? batchId = null, int page = 1, int pageSize = 50) =>
        {
            var r = await sender.Send(new GetProcessLogsQuery(garmentId, batchId, page < 1 ? 1 : page, pageSize < 1 ? 50 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<ProcessLogEntryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.process.scan");

        logs.MapPost("/", async (CreateProcessLogRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateProcessLogCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/process-logs/{r.Id}",
                new SingleResponse<ProcessLogEntryDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:warehouse.process.scan");

        // ── Quality Checks ────────────────────────────────────────────────────
        var qc = admin.MapGroup("/quality-checks").WithTags("Admin - Quality Checks");

        qc.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            Guid? garmentId = null, Guid? batchId = null, int page = 1, int pageSize = 20) =>
        {
            var r = await sender.Send(new GetQualityChecksQuery(garmentId, batchId, page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize), ct);
            return Results.Ok(new PaginatedListResponse<QualityCheckDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:qc.perform");

        qc.MapPost("/", async (CreateQualityCheckRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateQualityCheckCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/quality-checks/{r.Id}",
                new SingleResponse<QualityCheckDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:qc.perform");

        // ── Stock Reconciliation ──────────────────────────────────────────────
        var recon = admin.MapGroup("/stock-reconciliations").WithTags("Admin - Stock Reconciliation");

        recon.MapGet("/", async (
            [FromServices] ISender sender, CancellationToken ct,
            int page = 1, int pageSize = 20, string? status = null) =>
        {
            var r = await sender.Send(new GetStockReconsQuery(page < 1 ? 1 : page, pageSize < 1 ? 20 : pageSize, status), ct);
            return Results.Ok(new PaginatedListResponse<StockReconciliationDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:stockrecon.manage");

        recon.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new GetStockReconByIdQuery(id), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<StockReconciliationDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:stockrecon.manage");

        recon.MapPost("/", async (CreateStockReconciliationRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CreateStockReconCommand(req, u.UserId), ct);
            return Results.Created($"/api/v1/admin/stock-reconciliations/{r.Id}",
                new SingleResponse<StockReconciliationDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:stockrecon.manage");

        recon.MapPost("/{id:guid}/items", async (Guid id, AddReconItemRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new AddReconItemCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Created($"/api/v1/admin/stock-reconciliations/{id}/items/{r.Id}",
                new SingleResponse<StockReconciliationItemDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:stockrecon.manage");

        recon.MapPost("/{id:guid}/close", async (Guid id, CloseReconRequest req, ICurrentUser u, ISender sender, CancellationToken ct) =>
        {
            var r = await sender.Send(new CloseStockReconCommand(id, req, u.UserId), ct);
            return r is null ? Results.NotFound() : Results.Ok(new SingleResponse<StockReconciliationDto> { Status = true, Data = r });
        }).RequireAuthorization("permission:stockrecon.manage");

        return app;
    }
}
