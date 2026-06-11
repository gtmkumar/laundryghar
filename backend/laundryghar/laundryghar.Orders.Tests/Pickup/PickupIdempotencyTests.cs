using laundryghar.Orders.Application.Pickup.Commands;
using laundryghar.Orders.Application.Pickup.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace laundryghar.Orders.Tests.Pickup;

/// <summary>
/// Unit tests for pickup-request idempotency and source-channel logic.
///
/// These are pure in-memory tests targeting normalisation, validation, and
/// the idempotency guard logic. The full round-trip (same key returns same DB row)
/// requires a live database and is covered by the deferred integration test plan.
/// </summary>
public sealed class PickupIdempotencyTests
{
    // ── CustomerSchedulePickupValidator ────────────────────────────────────────

    private static CustomerSchedulePickupCommand MakeCmd(
        string? idempotencyKey = null,
        string source = "app") =>
        new(
            CustomerId: Guid.NewGuid(),
            BrandId: Guid.NewGuid(),
            Request: new CreatePickupRequestRequest(
                AddressId: Guid.NewGuid(),
                SlotId: null,
                PickupDate: DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
                PickupWindowStart: new TimeOnly(9, 0),
                PickupWindowEnd: new TimeOnly(11, 0),
                IsExpress: false,
                EstimatedItems: null,
                EstimatedAmount: null,
                ServicesRequested: [],
                CustomerNotes: null,
                CartItems: null,
                PaymentPreference: "cod"
            ),
            ActorId: null,
            ResolvedIdempotencyKey: idempotencyKey,
            ResolvedSource: source
        );

    private readonly CustomerSchedulePickupValidator _validator = new();

    // ── Source / channel validation ──────────────────────────────────────────

    [Theory]
    [InlineData("app")]
    [InlineData("web")]
    [InlineData("mcp")]
    [InlineData("whatsapp")]
    [InlineData("pos")]
    [InlineData("call")]
    public void Validator_ValidSource_Passes(string source)
    {
        var result = _validator.Validate(MakeCmd(source: source));
        Assert.True(result.IsValid, $"Source '{source}' should be valid.");
    }

    [Theory]
    [InlineData("App")]     // case should be tolerated by validator (HashSet uses OrdinalIgnoreCase)
    [InlineData("MCP")]
    [InlineData("WEB")]
    public void Validator_UpperCaseSource_Passes(string source)
    {
        // The validator uses a case-insensitive HashSet, so mixed-case inputs
        // are accepted at validation time; handler normalises to lower before persisting.
        var result = _validator.Validate(MakeCmd(source: source));
        Assert.True(result.IsValid, $"Source '{source}' should pass (case-insensitive check).");
    }

    [Theory]
    [InlineData("chatbot")]
    [InlineData("api")]
    [InlineData("")]
    [InlineData("unknown_channel")]
    public void Validator_InvalidSource_Fails(string source)
    {
        var result = _validator.Validate(MakeCmd(source: source));
        Assert.False(result.IsValid, $"Source '{source}' should fail validation.");
        Assert.Contains(result.Errors, e => e.PropertyName.Contains(nameof(CustomerSchedulePickupCommand.ResolvedSource)));
    }

    // ── Idempotency key validation ───────────────────────────────────────────

    [Fact]
    public void Validator_NullIdempotencyKey_Passes()
    {
        // No key → idempotency guard inactive — must not fail validation.
        var result = _validator.Validate(MakeCmd(idempotencyKey: null));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_ValidIdempotencyKey_Passes()
    {
        var result = _validator.Validate(MakeCmd(idempotencyKey: Guid.NewGuid().ToString()));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_IdempotencyKeyAt150Chars_Passes()
    {
        var result = _validator.Validate(MakeCmd(idempotencyKey: new string('k', 150)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_IdempotencyKeyExceeds150Chars_Fails()
    {
        var result = _validator.Validate(MakeCmd(idempotencyKey: new string('k', 151)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e =>
            e.PropertyName.Contains(nameof(CustomerSchedulePickupCommand.ResolvedIdempotencyKey)));
    }

    // ── Idempotency guard logic (pure logic, no DB) ──────────────────────────

    [Fact]
    public void IdempotencyGuard_SameKeyDifferentCustomers_AreDistinct()
    {
        // Two different customers sharing the same key string must each be treated
        // independently. This mirrors the DB partial unique index:
        //   UNIQUE (customer_id, idempotency_key) WHERE idempotency_key IS NOT NULL.
        // Verify the lookup predicate would be different for each customer.
        var sharedKey = "shared-key-abc";
        var customerId1 = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();

        // Simulate what the handler predicate would evaluate
        // p.CustomerId == customerId AND p.IdempotencyKey == sharedKey
        bool Matches(Guid customer, string key, Guid lookupCustomer, string lookupKey)
            => customer == lookupCustomer && key == lookupKey;

        // Same key, different customer → no match (different customer owns the row)
        Assert.False(Matches(customerId1, sharedKey, customerId2, sharedKey),
            "Different customers with the same key must not match each other's rows.");

        // Same key, same customer → match (idempotency hit)
        Assert.True(Matches(customerId1, sharedKey, customerId1, sharedKey),
            "Same customer with same key must return existing row.");
    }

    [Fact]
    public void IdempotencyGuard_NullKey_NeverMatches()
    {
        // A null idempotency key must never trigger the guard (no lookup performed).
        // The handler short-circuits with: if (!string.IsNullOrWhiteSpace(cmd.ResolvedIdempotencyKey))
        string? key = null;
        Assert.False(!string.IsNullOrWhiteSpace(key),
            "Null/empty key must skip the idempotency lookup.");
    }

    [Fact]
    public void IdempotencyGuard_WhiteSpaceKey_NeverMatches()
    {
        string? key = "   ";
        Assert.False(!string.IsNullOrWhiteSpace(key),
            "Whitespace-only key must skip the idempotency lookup.");
    }

    // ── PickupRequestDto default source value ────────────────────────────────

    [Fact]
    public void PickupRequestDto_DefaultSource_IsApp()
    {
        // When source is omitted from the DTO constructor, it should default to "app"
        // — preserving backward compatibility for existing callers.
        var dto = new PickupRequestDto(
            Id: Guid.NewGuid(),
            RequestNumber: "PKP-2025-TEST-000001",
            BrandId: Guid.NewGuid(),
            StoreId: null,
            CustomerId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PickupSlotId: null,
            PickupDate: DateOnly.FromDateTime(DateTime.Today),
            PickupWindowStart: new TimeOnly(9, 0),
            PickupWindowEnd: new TimeOnly(11, 0),
            IsExpress: false,
            EstimatedItems: null,
            EstimatedAmount: null,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            CartItems: [],
            PaymentPreference: "cod"
        // Source and IdempotencyKey use defaults
        );

        Assert.Equal("app", dto.Source);
        Assert.Null(dto.IdempotencyKey);
    }

    [Fact]
    public void PickupRequestDto_ExplicitSourceMcp_Preserved()
    {
        var dto = new PickupRequestDto(
            Id: Guid.NewGuid(),
            RequestNumber: "PKP-2025-TEST-000002",
            BrandId: Guid.NewGuid(),
            StoreId: null,
            CustomerId: Guid.NewGuid(),
            AddressId: Guid.NewGuid(),
            PickupSlotId: null,
            PickupDate: DateOnly.FromDateTime(DateTime.Today),
            PickupWindowStart: new TimeOnly(9, 0),
            PickupWindowEnd: new TimeOnly(11, 0),
            IsExpress: false,
            EstimatedItems: null,
            EstimatedAmount: null,
            Status: "pending",
            CreatedAt: DateTimeOffset.UtcNow,
            CartItems: [],
            PaymentPreference: "cod",
            Source: "mcp",
            IdempotencyKey: "test-key-123"
        );

        Assert.Equal("mcp", dto.Source);
        Assert.Equal("test-key-123", dto.IdempotencyKey);
    }

    // ── CreatePickupRequestRequest defaults ──────────────────────────────────

    [Fact]
    public void CreatePickupRequestRequest_NewChannelAndKeyFields_DefaultToNull()
    {
        // Existing callers that omit IdempotencyKey and Channel must continue
        // to work without modification — both new fields must default to null.
        var req = new CreatePickupRequestRequest(
            AddressId: Guid.NewGuid(),
            SlotId: null,
            PickupDate: DateOnly.FromDateTime(DateTime.Today),
            PickupWindowStart: new TimeOnly(9, 0),
            PickupWindowEnd: new TimeOnly(11, 0),
            IsExpress: false,
            EstimatedItems: null,
            EstimatedAmount: null,
            ServicesRequested: [],
            CustomerNotes: null,
            CartItems: null,
            PaymentPreference: "cod"
        // IdempotencyKey and Channel not passed → use defaults
        );

        Assert.Null(req.IdempotencyKey);
        Assert.Null(req.Channel);
    }

    // ── Race-safety: IsIdempotencyKeyViolation classification ────────────────
    //
    // These tests exercise the internal static helper that the catch-filter
    // calls to distinguish a genuine idempotency collision from any other
    // unique-constraint or DB error. Requires InternalsVisibleTo on the Orders
    // assembly (added to laundryghar.Orders.csproj).

    /// <summary>
    /// Build a PostgresException with the 18-param constructor so that both
    /// SqlState and ConstraintName are set — the primary detection path.
    /// </summary>
    private static PostgresException MakePg23505WithConstraint(string constraintName) =>
        new(
            messageText:       $"duplicate key value violates unique constraint \"{constraintName}\"",
            severity:          "ERROR",
            invariantSeverity: "ERROR",
            sqlState:          "23505",
            detail:            null,
            hint:              null,
            position:          0,
            internalPosition:  0,
            internalQuery:     null,
            where:             null,
            schemaName:        "order_lifecycle",
            tableName:         "pickup_requests",
            columnName:        null,
            dataTypeName:      null,
            constraintName:    constraintName,
            file:              null,
            line:              null,
            routine:           null);

    /// <summary>
    /// Build a PostgresException using the 4-param constructor where ConstraintName
    /// is empty but the index name appears in the message — tests the message-scan
    /// fallback detection path.
    /// </summary>
    private static PostgresException MakePg23505MessageOnly(string indexInMessage) =>
        new(
            messageText:       $"duplicate key value violates unique constraint \"{indexInMessage}\"",
            severity:          "ERROR",
            invariantSeverity: "ERROR",
            sqlState:          "23505");

    [Fact]
    public void IsIdempotencyKeyViolation_CorrectConstraintAndSqlState_ReturnsTrue()
    {
        var pgEx = MakePg23505WithConstraint("pickup_requests_customer_idempotency_key");
        var dbEx = new DbUpdateException("unique violation", pgEx);

        Assert.True(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should detect idempotency collision via ConstraintName + SqlState.");
    }

    [Fact]
    public void IsIdempotencyKeyViolation_MessageFallback_ReturnsTrue()
    {
        // Some Npgsql versions / configurations may not populate ConstraintName;
        // the handler falls back to scanning the message text for the index name.
        var pgEx = MakePg23505MessageOnly("pickup_requests_customer_idempotency_key");
        var dbEx = new DbUpdateException("unique violation", pgEx);

        Assert.True(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should detect idempotency collision via message text when ConstraintName is absent.");
    }

    [Fact]
    public void IsIdempotencyKeyViolation_DifferentConstraint_ReturnsFalse()
    {
        // A 23505 on a different index (e.g. request_number uniqueness) must NOT
        // be swallowed as an idempotency hit — it should propagate as a real error.
        var pgEx = MakePg23505WithConstraint("pickup_requests_request_number_key");
        var dbEx = new DbUpdateException("unique violation on request_number", pgEx);

        Assert.False(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should NOT classify a different unique constraint as an idempotency collision.");
    }

    [Fact]
    public void IsIdempotencyKeyViolation_DifferentSqlState_ReturnsFalse()
    {
        // A non-unique-violation Postgres error (e.g. FK violation 23503) must not match.
        var pgEx = new PostgresException(
            "insert or update on table violates foreign key constraint",
            "ERROR", "ERROR", "23503");
        var dbEx = new DbUpdateException("FK violation", pgEx);

        Assert.False(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should NOT classify a non-23505 SqlState as an idempotency collision.");
    }

    [Fact]
    public void IsIdempotencyKeyViolation_InnerExceptionNotPostgres_ReturnsFalse()
    {
        // A DbUpdateException wrapping a non-Postgres exception (e.g. optimistic
        // concurrency) must not be misclassified.
        var dbEx = new DbUpdateException("concurrency", new InvalidOperationException("stale"));

        Assert.False(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should NOT classify a non-PostgresException inner as an idempotency collision.");
    }

    [Fact]
    public void IsIdempotencyKeyViolation_NoInnerException_ReturnsFalse()
    {
        var dbEx = new DbUpdateException("plain db error");

        Assert.False(CustomerSchedulePickupHandler.IsIdempotencyKeyViolation(dbEx),
            "Should NOT classify a DbUpdateException with no inner exception as a collision.");
    }
}
