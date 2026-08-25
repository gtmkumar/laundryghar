using System.Runtime.CompilerServices;
using laundryghar.SharedDataModel.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace laundryghar.SharedDataModel.Persistence;

/// <summary>
/// <see cref="IBrandExportService"/> over <c>kernel.export_brand</c> (migration 0015).
///
/// <para>Reads through a raw <see cref="NpgsqlDataReader"/> rather than EF, for one reason worth
/// stating: EF would buffer. This yields each row as it arrives, so the response starts flowing
/// immediately and peak memory is one record regardless of how big the tenant is.</para>
/// </summary>
public sealed class BrandExportService : IBrandExportService
{
    private readonly LaundryGharDbContext _db;

    public BrandExportService(LaundryGharDbContext db) => _db = db;

    public async IAsyncEnumerable<BrandExportRecord> StreamAsync(
        Guid brandId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        var opened = conn.State != System.Data.ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);

        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT source, row_data::text FROM kernel.export_brand($1)";
            cmd.Parameters.Add(new NpgsqlParameter { Value = brandId });

            // SequentialAccess: read each column once, in order, without buffering the row.
            await using var reader = await cmd.ExecuteReaderAsync(
                System.Data.CommandBehavior.SequentialAccess, ct);

            while (await reader.ReadAsync(ct))
                yield return new BrandExportRecord(reader.GetString(0), reader.GetString(1));
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }
}
