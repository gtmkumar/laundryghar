namespace operations.Application.Orders.Invoices;

/// <summary>
/// Pure-static GST tax math for invoice generation.
/// Contains no I/O — suitable for direct unit testing.
///
/// Rules:
///   - 18% GST on taxable total (as configured in OrdersSettings).
///   - Intra-state: CGST 9% + SGST 9%  (isInterState = false, the default).
///   - Inter-state: IGST 18%, CGST = SGST = 0  (isInterState = true).
///   - Rounding: all individual amounts rounded to 2 decimal places;
///     grand_total = taxableTotal + cgst + sgst + igst - discounts + roundOff.
/// </summary>
public static class InvoiceTaxCalculator
{
    public const decimal DefaultTaxRate    = 18m;
    public const decimal DefaultHalfRate   = 9m;
    public const string  DefaultSacCode    = "999712";

    /// <summary>
    /// Returns the Indian Fiscal Year start year for a given UTC date.
    /// Indian FY runs April 1 – March 31.
    /// E.g. 2026-03-15 → FY 2025-26 → returns 2025.
    ///      2026-04-01 → FY 2026-27 → returns 2026.
    /// </summary>
    public static int IndianFiscalYear(DateOnly date)
        => date.Month >= 4 ? date.Year : date.Year - 1;

    /// <summary>
    /// Computes GST split for a given taxable total.
    /// Returns (cgstRate, cgstAmount, sgstRate, sgstAmount, igstRate, igstAmount).
    /// </summary>
    public static (decimal cgstRate, decimal cgstAmount,
                   decimal sgstRate, decimal sgstAmount,
                   decimal igstRate, decimal igstAmount)
        ComputeGst(decimal taxableTotal, bool isInterState = false)
    {
        if (isInterState)
        {
            var igst = Round(taxableTotal * DefaultTaxRate / 100m);
            return (0m, 0m, 0m, 0m, DefaultTaxRate, igst);
        }
        else
        {
            var cgst = Round(taxableTotal * DefaultHalfRate / 100m);
            var sgst = Round(taxableTotal * DefaultHalfRate / 100m);
            return (DefaultHalfRate, cgst, DefaultHalfRate, sgst, 0m, 0m);
        }
    }

    /// <summary>
    /// Formats the Indian Fiscal Year label as a 4-character string.
    /// E.g. FY start 2026 → "2627".
    /// </summary>
    public static string FiscalYearLabel(int fyStartYear)
    {
        var start = fyStartYear % 100;
        var end   = (fyStartYear + 1) % 100;
        return $"{start:D2}{end:D2}";
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
