using laundryghar.Orders.Application.Invoices;

namespace laundryghar.Orders.Tests.Invoices;

/// <summary>
/// Unit tests for invoice number format logic derived from InvoiceTaxCalculator helpers.
///
/// Expected format: INV-{FY}-{storeCode}-{seq:D6}
///   e.g. INV-2627-LGG-S45-001-000001
///
/// The actual sequence allocation happens in PostgreSQL (next_invoice_number SQL function).
/// These tests cover the C#-side FY computation + label formatting that feeds into the
/// SQL call, ensuring the correct fiscal year is passed.
/// </summary>
public sealed class InvoiceNumberFormatTests
{
    [Theory]
    // Format: (invoiceDate, expectedFyLabel)
    [InlineData(2026, 6, 15, "2627")]    // June 2026 → FY 2026-27 → "2627"
    [InlineData(2026, 3, 31, "2526")]    // Mar 31 2026 → still FY 2025-26 → "2526"
    [InlineData(2026, 4,  1, "2627")]    // Apr 1 2026 → FY 2026-27 → "2627"
    [InlineData(2025, 1,  1, "2425")]    // Jan 2025 → FY 2024-25 → "2425"
    public void FyLabel_MatchesExpected(int year, int month, int day, string expectedLabel)
    {
        var date = new DateOnly(year, month, day);
        var fy   = InvoiceTaxCalculator.IndianFiscalYear(date);
        var label = InvoiceTaxCalculator.FiscalYearLabel(fy);
        Assert.Equal(expectedLabel, label);
    }

    [Fact]
    public void FiscalYear_TransitionAt_April1()
    {
        // March 31 and April 1 should be in different fiscal years.
        var mar31 = new DateOnly(2026, 3, 31);
        var apr1  = new DateOnly(2026, 4, 1);

        Assert.NotEqual(
            InvoiceTaxCalculator.IndianFiscalYear(mar31),
            InvoiceTaxCalculator.IndianFiscalYear(apr1));
    }

    [Fact]
    public void FiscalYearLabel_Is4Chars()
    {
        foreach (var fyStart in Enumerable.Range(2020, 20))
        {
            var label = InvoiceTaxCalculator.FiscalYearLabel(fyStart);
            Assert.Equal(4, label.Length);
        }
    }
}
