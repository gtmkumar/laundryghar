using laundryghar.Orders.Application.Invoices.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace laundryghar.Orders.Application.Invoices;

/// <summary>
/// Renders a GST-compliant A4 tax invoice PDF using QuestPDF Community (MIT).
/// Layout: header (brand/store info, GSTIN), invoice meta box, items table,
///         GST summary box (CGST/SGST or IGST rows), grand total, footer note.
///
/// Keep this file self-contained so POS / mobile can restyle independently.
/// </summary>
public static class InvoicePdfRenderer
{
    private static readonly TextStyle LabelStyle  = TextStyle.Default.FontSize(8).FontColor(Colors.Grey.Darken2);
    private static readonly TextStyle ValueStyle  = TextStyle.Default.FontSize(9);
    private static readonly TextStyle HeadingStyle = TextStyle.Default.FontSize(11).Bold();
    private static readonly TextStyle TitleStyle  = TextStyle.Default.FontSize(14).Bold();

    static InvoicePdfRenderer()
    {
        // Community licence — no watermark for open-source/community use.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <summary>Generates the PDF and returns the raw bytes.</summary>
    public static byte[] Render(InvoiceDto invoice)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30, Unit.Point);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Element(ComposeHeader(invoice));
                page.Content().Element(ComposeContent(invoice));
                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("This is a computer-generated invoice. No signature required. | ").Style(LabelStyle);
                    x.Span($"SAC {invoice.SacCode} — Laundry & Dry-Cleaning Services").Style(LabelStyle);
                });
            });
        }).GeneratePdf();
    }

    // ── Header: supplier info + invoice title ─────────────────────────────────

    private static Action<IContainer> ComposeHeader(InvoiceDto inv) => c =>
    {
        c.BorderBottom(1).BorderColor(Colors.Grey.Lighten1).PaddingBottom(8).Row(row =>
        {
            // Left: supplier info
            row.RelativeItem(3).Column(col =>
            {
                col.Item().Text(inv.SupplierName).Style(HeadingStyle);
                col.Item().Text(inv.SupplierAddress).Style(ValueStyle).FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrWhiteSpace(inv.SupplierGstin))
                    col.Item().Text($"GSTIN: {inv.SupplierGstin}").Style(ValueStyle);
                else
                    col.Item().Text("GSTIN: Unregistered / Composition").Style(LabelStyle);
            });

            // Right: invoice title
            row.RelativeItem(2).AlignRight().Column(col =>
            {
                col.Item().Text("TAX INVOICE").Style(TitleStyle);
                col.Item().Text($"Invoice No: {inv.InvoiceNumber}").Style(ValueStyle);
                col.Item().Text($"Invoice Date: {inv.InvoiceDate:dd MMM yyyy}").Style(ValueStyle);
                col.Item().Text($"Place of Supply: {inv.PlaceOfSupply}").Style(ValueStyle);
            });
        });
    };

    // ── Content: customer box + items table + tax summary ─────────────────────

    private static Action<IContainer> ComposeContent(InvoiceDto inv) => c =>
    {
        c.Column(col =>
        {
            // Customer details
            col.Item().PaddingTop(10).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1);
                    cd.RelativeColumn(1);
                });

                t.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(inner =>
                {
                    inner.Item().Text("Bill To").Style(LabelStyle);
                    inner.Item().Text(inv.CustomerName).Style(ValueStyle).Bold();
                    inner.Item().Text(inv.CustomerPhone).Style(ValueStyle);
                    if (!string.IsNullOrWhiteSpace(inv.CustomerGstin))
                        inner.Item().Text($"GSTIN: {inv.CustomerGstin}").Style(ValueStyle);
                });

                t.Cell().Border(1).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(inner =>
                {
                    inner.Item().Text("Service Details").Style(LabelStyle);
                    inner.Item().Text($"SAC Code: {inv.SacCode}").Style(ValueStyle);
                    inner.Item().Text("Laundry & Dry-Cleaning Services").Style(ValueStyle);
                });
            });

            // Items table
            col.Item().PaddingTop(12).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(25);   // #
                    cd.RelativeColumn(5);    // Description
                    cd.RelativeColumn(1.5f); // Qty
                    cd.RelativeColumn(2);    // Unit Price
                    cd.RelativeColumn(2);    // Taxable Value
                });

                // Header row
                static void HeaderCell(IContainer c, string text)
                    => c.Background(Colors.Grey.Lighten3).BorderBottom(1).BorderColor(Colors.Grey.Medium)
                         .Padding(4).Text(text).Style(TextStyle.Default.FontSize(8).Bold());

                t.Header(h =>
                {
                    h.Cell().Element(c => HeaderCell(c, "#"));
                    h.Cell().Element(c => HeaderCell(c, "Description"));
                    h.Cell().Element(c => HeaderCell(c, "Qty"));
                    h.Cell().Element(c => HeaderCell(c, "Unit Price (₹)"));
                    h.Cell().Element(c => HeaderCell(c, "Taxable Value (₹)"));
                });

                for (int i = 0; i < inv.LineItems.Count; i++)
                {
                    var item = inv.LineItems[i];
                    var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten5;

                    t.Cell().Background(bg).Padding(4).Text((i + 1).ToString());
                    t.Cell().Background(bg).Padding(4).Text(item.Description);
                    t.Cell().Background(bg).Padding(4).AlignRight().Text($"{item.Qty} {item.Unit}");
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(item.UnitPrice.ToString("N2"));
                    t.Cell().Background(bg).Padding(4).AlignRight().Text(item.TaxableValue.ToString("N2"));
                }
            });

            // Tax summary
            col.Item().PaddingTop(12).AlignRight().Width(260).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(3);
                    cd.RelativeColumn(2);
                });

                void SummaryRow(string label, decimal amount, bool bold = false)
                {
                    var style = bold ? TextStyle.Default.FontSize(9).Bold() : TextStyle.Default.FontSize(9);
                    t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).Text(label).Style(style);
                    t.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(3).AlignRight()
                        .Text($"₹ {amount:N2}").Style(style);
                }

                SummaryRow("Subtotal", inv.Subtotal);
                if (inv.DiscountTotal > 0)
                    SummaryRow($"Discount", -inv.DiscountTotal);
                SummaryRow("Taxable Amount", inv.TaxableTotal);

                if (inv.IgstAmount > 0)
                {
                    SummaryRow($"IGST @ {inv.IgstRate:0.##}%", inv.IgstAmount);
                }
                else
                {
                    SummaryRow($"CGST @ {inv.CgstRate:0.##}%", inv.CgstAmount);
                    SummaryRow($"SGST @ {inv.SgstRate:0.##}%", inv.SgstAmount);
                }

                if (inv.RoundOff != 0)
                    SummaryRow("Round Off", inv.RoundOff);

                SummaryRow("Grand Total", inv.GrandTotal, bold: true);
            });

            // GST tax breakdown note
            col.Item().PaddingTop(8).Text(
                $"Amount in words: {AmountToWords(inv.GrandTotal)} Rupees Only")
                .Style(LabelStyle.Italic());
        });
    };

    // ── Minimal amount-to-words (Indian number system) ────────────────────────

    private static string AmountToWords(decimal amount)
    {
        var rupees = (long)Math.Floor(amount);
        var paise  = (int)Math.Round((amount - rupees) * 100);
        var words  = RupeesToWords(rupees);
        return paise > 0 ? $"{words} and {paise}/100" : words;
    }

    private static string RupeesToWords(long n)
    {
        if (n == 0) return "Zero";

        string[] ones = ["", "One", "Two", "Three", "Four", "Five", "Six", "Seven",
                         "Eight", "Nine", "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen",
                         "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen"];
        string[] tens = ["", "", "Twenty", "Thirty", "Forty", "Fifty",
                         "Sixty", "Seventy", "Eighty", "Ninety"];

        string Words(long num)
        {
            if (num == 0) return "";
            if (num < 20) return ones[num] + " ";
            if (num < 100) return tens[num / 10] + " " + Words(num % 10);
            if (num < 1000) return ones[num / 100] + " Hundred " + Words(num % 100);
            if (num < 100000) return Words(num / 1000) + "Thousand " + Words(num % 1000);
            if (num < 10000000) return Words(num / 100000) + "Lakh " + Words(num % 100000);
            return Words(num / 10000000) + "Crore " + Words(num % 10000000);
        }

        return Words(n).Trim();
    }
}
