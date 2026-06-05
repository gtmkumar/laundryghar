namespace laundryghar.Orders.Application.Common;

public sealed class OrdersSettings
{
    public const string SectionName = "Orders";
    public decimal TaxRatePercent { get; set; } = 18m;
    public decimal ExpressSurchargePercent { get; set; } = 50m;
    public string DefaultCurrencyCode { get; set; } = "INR";
}
