using System.Text;
using System.Text.Json;
using laundryghar.Utilities.Exceptions;
using laundryghar.Utilities.Middlewares.ExceptionsMiddleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace laundryghar.Commerce.Tests.Middlewares;

/// <summary>
/// DEF-4 regression: the global ExceptionHandler must translate exceptions into clean,
/// non-leaky HTTP envelopes:
///   • DbUpdateException 23505 → 409 "A record with the same value already exists."
///   • DbUpdateException 23514 → 422 "Value violates a data constraint."
///   • DbUpdateException 22P02 → 422 "Invalid value format."
///   • other DbUpdateException → 400 generic (NEVER the raw Npgsql message)
///   • KeyNotFoundException    → 404
///   • BadHttpRequestException → 400 "Malformed request body."
/// </summary>
public sealed class ExceptionHandlerMappingTests
{
    // Mimics Npgsql.PostgresException's public string SqlState property without a
    // package dependency (ExceptionHandler reads SqlState by reflection).
    private sealed class FakePostgresException : Exception
    {
        public FakePostgresException(string sqlState, string message) : base(message)
            => SqlState = sqlState;
        public string SqlState { get; }
    }

    private static async Task<(int Status, string Body)> RunAsync(Exception toThrow)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.Path = "/api/v1/admin/x";
        var responseBody = new MemoryStream();
        ctx.Response.Body = responseBody;

        RequestDelegate next = _ => throw toThrow;
        var handler = new ExceptionHandler(next, NullLogger<ExceptionHandler>.Instance);

        await handler.Invoke(ctx);

        responseBody.Seek(0, SeekOrigin.Begin);
        var body = Encoding.UTF8.GetString(responseBody.ToArray());
        return (ctx.Response.StatusCode, body);
    }

    private static DbUpdateException DbWith(string sqlState, string rawNpgsqlMessage)
        => new("An error occurred while saving the entity changes.",
               new FakePostgresException(sqlState, rawNpgsqlMessage));

    [Fact]
    public async Task UniqueViolation_MapsTo409_WithoutLeakingRawMessage()
    {
        const string raw = "23505: duplicate key value violates unique constraint \"ix_users_email\"";
        var (status, body) = await RunAsync(DbWith("23505", raw));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Contains("A record with the same value already exists.", body);
        Assert.DoesNotContain("ix_users_email", body);
        Assert.DoesNotContain("23505", body);
    }

    [Fact]
    public async Task CheckViolation_MapsTo422()
    {
        var (status, body) = await RunAsync(
            DbWith("23514", "23514: new row violates check constraint \"chk_status\""));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.Contains("Value violates a data constraint.", body);
        Assert.DoesNotContain("chk_status", body);
    }

    [Fact]
    public async Task InvalidTextRepresentation_MapsTo422()
    {
        var (status, body) = await RunAsync(
            DbWith("22P02", "22P02: invalid input value for enum order_status: \"bogus\""));

        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
        Assert.Contains("Invalid value format.", body);
        Assert.DoesNotContain("order_status", body);
    }

    [Fact]
    public async Task OtherDbUpdate_MapsTo400_Generic()
    {
        var (status, body) = await RunAsync(
            DbWith("23503", "23503: foreign key violation on table \"orders\""));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.DoesNotContain("orders", body);
        Assert.DoesNotContain("23503", body);
    }

    [Fact]
    public async Task KeyNotFound_MapsTo404()
    {
        var (status, _) = await RunAsync(new KeyNotFoundException("tagCode 'XYZ' not found"));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task BadHttpRequest_MapsTo400_CleanMessage()
    {
        var (status, body) = await RunAsync(
            new BadHttpRequestException("Implicit body inferred for parameter ... but JSON was object not array"));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        Assert.Contains("Malformed request body.", body);
    }

    [Fact]
    public async Task ValidationException_MapsTo422()
    {
        var (status, _) = await RunAsync(
            new ValidationException(new Dictionary<string, string[]> { ["Name"] = ["required"] }));
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, status);
    }

    [Fact]
    public async Task UnknownException_MapsTo500_Generic()
    {
        var (status, body) = await RunAsync(new InvalidOperationException("super secret internal detail"));
        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.DoesNotContain("super secret internal detail", body);
    }
}
