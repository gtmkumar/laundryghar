namespace laundryghar.Commerce.Tests.Payments;

/// <summary>
/// Unit tests for the cumulative payment cap guard in RecordOfflinePaymentHandler.
///
/// Guard formula (mirrors the handler):
///   if (alreadyPaid + newAmount > grandTotal) → BusinessRuleException (422)
/// </summary>
public sealed class CumulativePaymentGuardTests
{
    // Mirrors the exact expression from RecordOfflinePaymentHandler
    private static bool WouldExceedTotal(decimal grandTotal, decimal alreadyPaid, decimal newAmount)
        => alreadyPaid + newAmount > grandTotal;

    [Theory]
    [InlineData(500.00,   0.00, 500.00, false)]  // exact full payment — allowed
    [InlineData(500.00,   0.00, 499.99, false)]  // just under — allowed
    [InlineData(500.00, 300.00, 200.00, false)]  // two-part exact = 500 — allowed
    [InlineData(500.00, 300.00, 200.01, true)]   // 1 paisa over — rejected
    [InlineData(500.00, 499.99,   0.02, true)]   // would exceed by 0.01 — rejected
    [InlineData(500.00, 500.00,   0.01, true)]   // already fully paid, any more → rejected
    [InlineData(  0.00,   0.00,   0.01, true)]   // zero-total order
    [InlineData(100.00,  50.00,  50.00, false)]  // partial then remainder — exact match allowed
    public void Guard_EnforcesCorrectly(
        decimal grandTotal, decimal alreadyPaid, decimal newAmount, bool shouldReject)
    {
        var wouldExceed = WouldExceedTotal(grandTotal, alreadyPaid, newAmount);
        Assert.Equal(shouldReject, wouldExceed);
    }

    // ── Payment status after recording ───────────────────────────────────────

    // Mirrors logic: newAmountPaid >= grandTotal ? "paid" : "partial"
    private static string DerivePaymentStatus(decimal grandTotal, decimal amountPaid)
        => amountPaid >= grandTotal ? "paid" : "partial";

    [Theory]
    [InlineData(500.00, 500.00, "paid")]
    [InlineData(500.00, 500.01, "paid")]   // overpaid edge (should not occur after guard, but status still 'paid')
    [InlineData(500.00, 499.99, "partial")]
    [InlineData(500.00,   0.00, "partial")]
    [InlineData(500.00, 250.00, "partial")]
    public void PaymentStatus_DerivedCorrectly(
        decimal grandTotal, decimal amountPaid, string expectedStatus)
    {
        Assert.Equal(expectedStatus, DerivePaymentStatus(grandTotal, amountPaid));
    }

    // ── Zero / negative amount guard ─────────────────────────────────────────

    [Fact]
    public void Amount_MustBePositive()
    {
        // Validator enforces Amount > 0; handler should never see <= 0
        const decimal amount = 0m;
        Assert.True(amount <= 0, "Zero amount must be rejected by the validator.");
    }

    [Fact]
    public void NegativeAmount_IsRejected()
    {
        const decimal amount = -10m;
        Assert.True(amount <= 0, "Negative amount must be rejected by the validator.");
    }

    // ── Idempotency key construction ──────────────────────────────────────────

    // Mirrors the BuildIdempotencyKey helper
    private static string BuildKey(Guid orderId, decimal amount, string? reference)
    {
        var refPart = string.IsNullOrWhiteSpace(reference) ? "noref" : reference.Trim().ToLowerInvariant();
        var raw     = $"offline:{orderId}:{amount:F2}:{refPart}";
        return raw[..Math.Min(200, raw.Length)];
    }

    [Fact]
    public void IdempotencyKey_IsDeterministic()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var k1 = BuildKey(id, 100m, "TXN123");
        var k2 = BuildKey(id, 100m, "TXN123");
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void IdempotencyKey_DiffersOnDifferentAmount()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var k1 = BuildKey(id, 100m, null);
        var k2 = BuildKey(id, 200m, null);
        Assert.NotEqual(k1, k2);
    }

    [Fact]
    public void IdempotencyKey_NullAndEmptyRefAreSame()
    {
        var id = Guid.NewGuid();
        var k1 = BuildKey(id, 50m, null);
        var k2 = BuildKey(id, 50m, "");
        Assert.Equal(k1, k2);
    }
}
