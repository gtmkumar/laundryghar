namespace operations.Application.Catalog.Pricing.Dtos;

// ── Value-price slabs (GH #22) ────────────────────────────────────────────────

public sealed record ValueSlabDto(
    Guid Id,
    Guid BrandId,
    /// <summary>Null = brand-wide lane (any service). Non-null = service-scoped slab.</summary>
    Guid? ServiceId,
    string? ServiceName,
    decimal MinValue,
    /// <summary>Null = open-ended top slab.</summary>
    decimal? MaxValue,
    decimal Price,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record CreateValueSlabRequest(
    Guid? ServiceId,
    decimal MinValue,
    decimal? MaxValue,
    decimal Price
);

public sealed record UpdateValueSlabRequest(
    Guid? ServiceId,
    decimal MinValue,
    decimal? MaxValue,
    decimal Price,
    string Status
);
