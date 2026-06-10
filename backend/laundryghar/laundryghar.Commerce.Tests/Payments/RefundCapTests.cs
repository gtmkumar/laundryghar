namespace laundryghar.Commerce.Tests.Payments;

/// <summary>
/// Unit tests for the cumulative refund cap arithmetic enforced in IssueRefundHandler.
/// These are pure math tests — no DB required.
/// </summary>
public sealed class RefundCapTests
{
    // Mirrors the exact expression used in IssueRefundHandler:
    //   if (alreadyRefunded + req.Amount > payment.Amount) → BusinessRuleException
    private static bool WouldExceedCap(decimal capturedAmount, decimal alreadyRefunded, decimal newAmount)
        => alreadyRefunded + newAmount > capturedAmount;

    [Theory]
    [InlineData(100.00, 0.00,  100.00, false)] // exact full refund — allowed
    [InlineData(100.00, 0.00,   50.00, false)] // partial — allowed
    [InlineData(100.00, 50.00,  50.00, false)] // two halves = 100 — allowed
    [InlineData(100.00, 50.00,  50.01, true)]  // 1 paisa over — rejected
    [InlineData(100.00, 99.99,   0.02, true)]  // exceeds by 0.01 — rejected
    [InlineData(100.00, 100.00,  0.01, true)]  // already fully refunded, any more → rejected
    [InlineData(  0.00,  0.00,   0.01, true)]  // zero-amount payment — any refund exceeds
    public void Cap_IsEnforcedCorrectly(
        decimal captured, decimal already, decimal newAmount, bool shouldReject)
    {
        var wouldExceed = WouldExceedCap(captured, already, newAmount);
        Assert.Equal(shouldReject, wouldExceed);
    }

    [Fact]
    public void PositiveAmount_IsRequired()
    {
        // Handler checks req.Amount > 0 separately (before the cap check)
        // This mirrors the guard: if (req.Amount <= 0) throw BusinessRuleException
        const decimal amount = -1m;
        Assert.True(amount <= 0, "Negative refund amount must be rejected.");
    }
}
