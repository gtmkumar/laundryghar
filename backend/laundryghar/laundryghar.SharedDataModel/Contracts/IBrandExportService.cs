namespace laundryghar.SharedDataModel.Contracts;

/// <summary>One record in a provider's export: which table it came from, and the row as JSON.</summary>
public sealed record BrandExportRecord(string Source, string Json);

/// <summary>
/// §8.2: a company may "export their data at any time; take it with them if they leave."
///
/// <para>Streams rather than materialising. A real tenant's export is tens of thousands of rows
/// across ~120 tables; building that in memory to hand back one JSON blob is how an export endpoint
/// becomes an out-of-memory incident on the day a large customer leaves — which is the worst possible
/// day for it to fail.</para>
/// </summary>
public interface IBrandExportService
{
    /// <summary>
    /// Every record belonging to <paramref name="brandId"/>, table by table.
    ///
    /// <para>Callers MUST have already established the caller's right to this brand — the underlying
    /// function is SECURITY DEFINER and trusts its argument, because a full export deliberately
    /// includes rows (the brand's own registration record) that ordinary RLS hides.</para>
    /// </summary>
    IAsyncEnumerable<BrandExportRecord> StreamAsync(Guid brandId, CancellationToken ct = default);
}
