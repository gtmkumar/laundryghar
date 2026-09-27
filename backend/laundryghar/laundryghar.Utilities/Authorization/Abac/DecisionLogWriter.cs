using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace laundryghar.Utilities.Authorization.Abac;

/// <summary>One row destined for <c>authz.decision_log</c>.</summary>
public sealed record AbacDecisionRecord(
    DateTimeOffset OccurredAt,
    Guid? BrandId,
    Guid? UserId,
    Guid? CustomerId,
    string? TokenUse,
    string ResourceType,
    Guid? ResourceId,
    string Action,
    string Decision,
    string Mode,
    string? MatchedPolicy,
    string? Reason,
    string? AttributesJson,
    int LatencyMicroseconds,
    /// <summary>What the existing permission-claim gate decided for the SAME request (A6.2).
    /// Null when the endpoint carried no permission requirement to compare against.</summary>
    bool? RbacAllowed);

public interface IDecisionLogWriter
{
    /// <summary>Queue a decision. Never blocks the request and never throws — an authorization
    /// decision must not fail because its audit row could not be queued.</summary>
    void Enqueue(AbacDecisionRecord record);
}

/// <summary>
/// Batched, out-of-band writer for the decision log (A1.5).
///
/// <para><b>Why a channel and not a synchronous INSERT.</b> The log records EVERY decision, permit
/// and deny, read and write — that is the whole point of it, since
/// <c>AuditSaveChangesInterceptor</c> fires only on writes and so records no reads at all. At that
/// volume a synchronous insert would put a network round trip on the authorization path of every
/// request, which is precisely the budget A9.1 has to defend. So decisions go onto a bounded
/// channel and a background loop writes them in batches.</para>
///
/// <para><b>What is given up, deliberately.</b> The channel is bounded and drops the OLDEST record
/// when full. A dropped record is a lost audit row, which is a real cost — but the alternative,
/// blocking the request until the log catches up, converts a logging backlog into an outage. The
/// drop is counted and logged so the loss is visible rather than silent.</para>
/// </summary>
public sealed class ChannelDecisionLogWriter : BackgroundService, IDecisionLogWriter
{
    private const int Capacity = 8192;
    private const int BatchSize = 200;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly Channel<AbacDecisionRecord> _channel =
        Channel.CreateBounded<AbacDecisionRecord>(new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

    private readonly IAbacConnectionFactory _connections;
    private readonly ILogger<ChannelDecisionLogWriter> _log;
    private long _dropped;

    public ChannelDecisionLogWriter(
        IAbacConnectionFactory connections, ILogger<ChannelDecisionLogWriter> log)
    {
        _connections = connections;
        _log = log;
    }

    public void Enqueue(AbacDecisionRecord record)
    {
        if (!_channel.Writer.TryWrite(record) && Interlocked.Increment(ref _dropped) % 1000 == 1)
            _log.LogWarning(
                "ABAC decision log is saturated; {Dropped} records dropped so far.",
                Interlocked.Read(ref _dropped));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AbacDecisionRecord>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await _channel.Reader.WaitToReadAsync(stoppingToken)) break;

                using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                window.CancelAfter(FlushInterval);

                batch.Clear();
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var record))
                    batch.Add(record);

                if (batch.Count > 0) await FlushAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // A logging failure must never take the host down, and must never be silent.
                _log.LogError(e, "ABAC decision log flush failed; {Count} records lost.", batch.Count);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        // Drain whatever is left so a graceful shutdown does not discard the tail.
        batch.Clear();
        while (batch.Count < BatchSize && _channel.Reader.TryRead(out var last)) batch.Add(last);
        if (batch.Count > 0)
        {
            try { await FlushAsync(batch, CancellationToken.None); }
            catch (Exception e) { _log.LogError(e, "ABAC decision log final flush failed."); }
        }
    }

    private async Task FlushAsync(List<AbacDecisionRecord> batch, CancellationToken ct)
    {
        await using var conn = await _connections.OpenAsync(ct);

        // Binary COPY: one round trip for the whole batch. The column list and its order must match
        // the CopyRow writes below exactly — Npgsql cannot check that for us.
        await using var writer = await conn.BeginBinaryImportAsync(
            """
            COPY authz.decision_log
              (occurred_at, brand_id, user_id, customer_id, token_use, resource_type, resource_id,
               action, decision, mode, matched_policy, reason, attributes, latency_us,
               rbac_allowed)
            FROM STDIN (FORMAT BINARY)
            """, ct);

        foreach (var r in batch)
        {
            await writer.StartRowAsync(ct);
            await writer.WriteAsync(r.OccurredAt, NpgsqlDbType.TimestampTz, ct);
            await WriteNullableAsync(writer, r.BrandId, NpgsqlDbType.Uuid, ct);
            await WriteNullableAsync(writer, r.UserId, NpgsqlDbType.Uuid, ct);
            await WriteNullableAsync(writer, r.CustomerId, NpgsqlDbType.Uuid, ct);
            await WriteNullableAsync(writer, r.TokenUse, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(r.ResourceType, NpgsqlDbType.Varchar, ct);
            await WriteNullableAsync(writer, r.ResourceId, NpgsqlDbType.Uuid, ct);
            await writer.WriteAsync(r.Action, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(r.Decision, NpgsqlDbType.Varchar, ct);
            await writer.WriteAsync(r.Mode, NpgsqlDbType.Varchar, ct);
            await WriteNullableAsync(writer, r.MatchedPolicy, NpgsqlDbType.Varchar, ct);
            await WriteNullableAsync(writer, r.Reason, NpgsqlDbType.Text, ct);
            await WriteNullableAsync(writer, r.AttributesJson, NpgsqlDbType.Jsonb, ct);
            await writer.WriteAsync(r.LatencyMicroseconds, NpgsqlDbType.Integer, ct);
            await WriteNullableAsync(writer, r.RbacAllowed, NpgsqlDbType.Boolean, ct);
        }

        await writer.CompleteAsync(ct);
    }

    private static async Task WriteNullableAsync<T>(
        NpgsqlBinaryImporter writer, T? value, NpgsqlDbType type, CancellationToken ct)
    {
        if (value is null) await writer.WriteNullAsync(ct);
        else await writer.WriteAsync(value, type, ct);
    }
}

/// <summary>Serialises the attribute bag for the decision log, minus anything not worth storing.</summary>
public static class AbacAttributeSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The bag as jsonb. <c>subject.permissions</c> is dropped: it can run to 170 codes on a
    /// platform admin, which would make the log an order of magnitude larger than the decisions it
    /// records while adding nothing a reader needs — the matched policy already names what mattered.
    /// </summary>
    public static string? Serialize(AbacAttributeBag bag)
    {
        try
        {
            var trimmed = bag.Values
                .Where(kv => kv.Key != AbacAttributeKeys.SubjectPermissions
                          && kv.Key != AbacAttributeKeys.ResourceTypeKey)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            return JsonSerializer.Serialize(trimmed, Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
