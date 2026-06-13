using laundryghar.Orders.Application.Pickup.Commands;

namespace laundryghar.Orders.Tests.Pickup;

/// <summary>
/// Unit tests for the pickup-leg COD-cash decision logic
/// (<see cref="AssignPickupHandler.ResolvePickupCodAmount"/>) that drives both:
///   1. assign-time seeding of DeliveryAssignment.CodAmount, and
///   2. the rider's collect transition stamping cod_amount + cod_collected_at.
///
/// The DB-touching handler glue (loading the pickup request, ??= idempotency on the
/// assignment) is covered by the deferred live E2E plan; these target the pure rule
/// and the stamping decision it produces. The rule mirrors the delivery-leg COD model
/// so both feed the SAME rider-cash settlement pipeline (cod_amount != null && settlement_id == null).
/// </summary>
public sealed class PickupCodCollectionTests
{
    // ── COD due: cash preference + positive estimate → copy the amount ──────────

    [Fact]
    public void Cod_PositiveEstimate_ReturnsAmount()
    {
        Assert.Equal(450m, AssignPickupHandler.ResolvePickupCodAmount("cod", 450m));
    }

    [Theory]
    [InlineData("COD")]
    [InlineData("Cod")]
    [InlineData(" cod ")]
    public void Cod_PreferenceIsCaseAndWhitespaceInsensitive(string pref)
    {
        Assert.Equal(100m, AssignPickupHandler.ResolvePickupCodAmount(pref, 100m));
    }

    // ── No-op cases: nothing for the rider to collect → null ────────────────────

    [Theory]
    [InlineData("wallet")]        // settled from wallet, not cash
    [InlineData("upi-deferred")]  // collected when the order is confirmed
    [InlineData("")]
    [InlineData(null)]
    public void NonCodPreference_ReturnsNull(string? pref)
    {
        Assert.Null(AssignPickupHandler.ResolvePickupCodAmount(pref, 500m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void Cod_ZeroOrNegativeEstimate_ReturnsNull(decimal amount)
    {
        Assert.Null(AssignPickupHandler.ResolvePickupCodAmount("cod", amount));
    }

    [Fact]
    public void Cod_NullEstimate_ReturnsNull()
    {
        // COD preferred but no estimated amount yet — nothing to collect.
        Assert.Null(AssignPickupHandler.ResolvePickupCodAmount("cod", null));
    }

    // ── Collect-stamp decision: the rule decides whether cod_collected_at is set ─
    // The handler stamps cod_amount/cod_collected_at exactly when the resolved amount
    // is > 0 (cod is > 0m). These assert that gate for the meaningful inputs.

    [Fact]
    public void CollectStamp_FiresOnlyWhenCodDue()
    {
        // Cash due → stamp.
        Assert.True(AssignPickupHandler.ResolvePickupCodAmount("cod", 250m) is > 0m);
        // Prepaid via wallet → no stamp.
        Assert.False(AssignPickupHandler.ResolvePickupCodAmount("wallet", 250m) is > 0m);
        // COD but zero due → no stamp.
        Assert.False(AssignPickupHandler.ResolvePickupCodAmount("cod", 0m) is > 0m);
    }

    // ── Idempotency: ??= on the assignment means a re-tap keeps the first amount ─

    [Fact]
    public void Collect_ReTap_KeepsFirstStampedValues()
    {
        // Simulates the handler's `da.CodAmount ??= cod; da.CodCollectedAt ??= now;`.
        decimal? codAmount = null;
        DateTimeOffset? collectedAt = null;
        var firstAt = DateTimeOffset.UtcNow;
        var resolved = AssignPickupHandler.ResolvePickupCodAmount("cod", 300m);

        // First collect.
        if (resolved is > 0m) { codAmount ??= resolved; collectedAt ??= firstAt; }
        // Second collect (re-tap) a minute later — must NOT overwrite.
        var secondAt = firstAt.AddMinutes(1);
        if (resolved is > 0m) { codAmount ??= resolved; collectedAt ??= secondAt; }

        Assert.Equal(300m, codAmount);
        Assert.Equal(firstAt, collectedAt);
    }
}
