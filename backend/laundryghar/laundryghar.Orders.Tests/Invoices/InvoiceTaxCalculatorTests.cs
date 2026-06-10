using laundryghar.Orders.Application.Invoices;

namespace laundryghar.Orders.Tests.Invoices;

/// <summary>
/// Unit tests for InvoiceTaxCalculator — pure math, no I/O.
/// Covers: GST split (intra-state CGST+SGST, inter-state IGST),
///         Indian fiscal year computation, FY label formatting.
/// </summary>
public sealed class InvoiceTaxCalculatorTests
{
    // ── IndianFiscalYear ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, 4,  1, 2026)]   // Apr 1 → new FY starts
    [InlineData(2026, 3, 31, 2025)]   // Mar 31 → still old FY
    [InlineData(2026, 1,  1, 2025)]   // Jan → old FY
    [InlineData(2025, 12, 31, 2025)]  // Dec 31 FY 2025-26
    [InlineData(2025, 4,  1, 2025)]   // Apr 1 FY 2025-26
    public void IndianFiscalYear_ReturnsCorrectStartYear(int year, int month, int day, int expectedFy)
    {
        var date = new DateOnly(year, month, day);
        Assert.Equal(expectedFy, InvoiceTaxCalculator.IndianFiscalYear(date));
    }

    // ── FiscalYearLabel ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2026, "2627")]
    [InlineData(2025, "2526")]
    [InlineData(2099, "9900")]   // century rollover: 2099→2100, both mod 100
    [InlineData(2000, "0001")]
    public void FiscalYearLabel_FormatsCorrectly(int fyStartYear, string expectedLabel)
    {
        Assert.Equal(expectedLabel, InvoiceTaxCalculator.FiscalYearLabel(fyStartYear));
    }

    // ── ComputeGst — intra-state (CGST+SGST) ──────────────────────────────────

    [Theory]
    [InlineData(100.00,  9.00,  9.00)]   // exact 9% each
    [InlineData(194.00, 17.46, 17.46)]   // 9% of 194 = 17.46
    [InlineData(342.00, 30.78, 30.78)]   // 9% of 342 = 30.78
    [InlineData(  0.00,  0.00,  0.00)]   // zero taxable
    public void ComputeGst_IntraState_SplitsCgstAndSgst(
        decimal taxable, decimal expectedCgst, decimal expectedSgst)
    {
        var (cgstRate, cgst, sgstRate, sgst, igstRate, igst) =
            InvoiceTaxCalculator.ComputeGst(taxable, isInterState: false);

        Assert.Equal(InvoiceTaxCalculator.DefaultHalfRate, cgstRate);
        Assert.Equal(InvoiceTaxCalculator.DefaultHalfRate, sgstRate);
        Assert.Equal(0m, igstRate);
        Assert.Equal(expectedCgst, cgst);
        Assert.Equal(expectedSgst, sgst);
        Assert.Equal(0m, igst);
    }

    // ── ComputeGst — inter-state (IGST only) ──────────────────────────────────

    [Theory]
    [InlineData(100.00, 18.00)]
    [InlineData(500.00, 90.00)]
    [InlineData(  0.00,  0.00)]
    public void ComputeGst_InterState_OnlyIgst(decimal taxable, decimal expectedIgst)
    {
        var (cgstRate, cgst, sgstRate, sgst, igstRate, igst) =
            InvoiceTaxCalculator.ComputeGst(taxable, isInterState: true);

        Assert.Equal(0m, cgstRate);
        Assert.Equal(0m, cgst);
        Assert.Equal(0m, sgstRate);
        Assert.Equal(0m, sgst);
        Assert.Equal(InvoiceTaxCalculator.DefaultTaxRate, igstRate);
        Assert.Equal(expectedIgst, igst);
    }

    // ── Rounding — amounts rounded to 2dp ────────────────────────────────────

    [Fact]
    public void ComputeGst_RoundsToTwoDecimalPlaces()
    {
        // 9% of 333 = 29.97 exactly — no rounding edge
        var (_, cgst, _, sgst, _, _) = InvoiceTaxCalculator.ComputeGst(333m, isInterState: false);
        Assert.Equal(29.97m, cgst);
        Assert.Equal(29.97m, sgst);
    }

    [Fact]
    public void ComputeGst_RoundsHalfUp()
    {
        // 9% of 16.67 = 1.5003 → rounds to 1.50
        var (_, cgst, _, _, _, _) = InvoiceTaxCalculator.ComputeGst(16.67m, isInterState: false);
        Assert.Equal(1.50m, cgst);
    }
}
