using laundryghar.Logistics.Application.RiderCod;

namespace laundryghar.Logistics.Tests.Validators;

/// <summary>
/// Validator unit tests for SettleRiderCodValidator.
///
/// Key invariants:
///   - Reference: max 100 characters (null is allowed — field is optional)
///   - Notes: max 500 characters (null is allowed — field is optional)
///   - Amount is NOT validated here — it is derived from outstanding DB rows,
///     not caller-supplied, so any caller-provided amount should not be rejected.
/// </summary>
public sealed class RiderCodValidatorTests
{
    private readonly SettleRiderCodValidator _validator = new();

    private static SettleRiderCodCommand Cmd(string? reference = null, string? notes = null) =>
        new(Guid.NewGuid(), new SettleRiderCodRequest(null, reference, notes), null);

    // ────────────────────────────────────────────────────────────────────────────
    // Happy paths — both fields null, exactly at boundary
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Settle_BothFieldsNull_Passes()
        => Assert.True(_validator.Validate(Cmd()).IsValid);

    [Fact]
    public void Settle_ReferenceExactly100_Passes()
        => Assert.True(_validator.Validate(Cmd(reference: new string('R', 100))).IsValid);

    [Fact]
    public void Settle_NotesExactly500_Passes()
        => Assert.True(_validator.Validate(Cmd(notes: new string('N', 500))).IsValid);

    [Fact]
    public void Settle_BothFieldsProvided_WithinBounds_Passes()
        => Assert.True(_validator.Validate(Cmd("REF-2024-001", "Settled at store counter.")).IsValid);

    // ────────────────────────────────────────────────────────────────────────────
    // Boundary violations
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Settle_Reference101Chars_Fails()
    {
        var result = _validator.Validate(Cmd(reference: new string('R', 101)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Reference"));
    }

    [Fact]
    public void Settle_Notes501Chars_Fails()
    {
        var result = _validator.Validate(Cmd(notes: new string('N', 501)));
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Notes"));
    }

    [Fact]
    public void Settle_BothFieldsOverLimit_FailsBoth()
    {
        var result = _validator.Validate(Cmd(
            reference: new string('R', 101),
            notes: new string('N', 501)));
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
    }
}
