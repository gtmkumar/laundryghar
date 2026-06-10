namespace laundryghar.Commerce.Tests.Payments;

/// <summary>
/// Regression tests that guard the string literals written into commerce.payment_refunds
/// against the DB CHECK constraints.
///
/// DB CHECK definitions (verified 2026-06-10 via pg_constraint):
///   payment_refunds_status_check      : pending | approved | processing | succeeded | failed | rejected
///   payment_refunds_refund_method_check: original | wallet | bank_transfer | store_credit
///   payment_refunds_refund_type_check  : full | partial | goodwill | dispute_loss
///
/// If a new literal is ever added here, the migration adding it to the CHECK must land first.
/// </summary>
public sealed class RefundConstraintLiteralTests
{
    // ─── Allowed sets mirrored from the DB CHECK constraints ───────────────────

    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal)
    {
        "pending", "approved", "processing", "succeeded", "failed", "rejected"
    };

    private static readonly HashSet<string> AllowedRefundMethods = new(StringComparer.Ordinal)
    {
        "original", "wallet", "bank_transfer", "store_credit"
    };

    private static readonly HashSet<string> AllowedRefundTypes = new(StringComparer.Ordinal)
    {
        "full", "partial", "goodwill", "dispute_loss"
    };

    // ─── Literals used by AdminPaymentHandlers (IssueRefundHandler) ────────────

    [Fact]
    public void AdminPaymentHandlers_GatewayPath_RefundMethod_IsInAllowedSet()
        => Assert.Contains("original", AllowedRefundMethods);

    [Fact]
    public void AdminPaymentHandlers_WalletPath_RefundMethod_IsInAllowedSet()
        => Assert.Contains("wallet", AllowedRefundMethods);

    [Fact]
    public void AdminPaymentHandlers_ProcessedStatus_IsInAllowedSet()
        => Assert.Contains("succeeded", AllowedStatuses);

    // ─── Literals used by CancelOrderCommand ───────────────────────────────────

    [Fact]
    public void CancelOrderCommand_RefundType_IsInAllowedSet()
        => Assert.Contains("full", AllowedRefundTypes);

    [Fact]
    public void CancelOrderCommand_InitialStatus_IsInAllowedSet()
        => Assert.Contains("pending", AllowedStatuses);

    // ─── Guard: formerly-wrong values must NOT be in the allowed sets ──────────

    [Fact]
    public void Legacy_GatewayRefundMethod_IsNotAllowed()
        => Assert.DoesNotContain("gateway", AllowedRefundMethods);

    [Fact]
    public void Legacy_CompletedStatus_IsNotAllowed()
        => Assert.DoesNotContain("completed", AllowedStatuses);

    [Fact]
    public void Legacy_GatewayRefundType_IsNotAllowed()
        => Assert.DoesNotContain("gateway", AllowedRefundTypes);
}
