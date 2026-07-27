namespace StockSharp.Ventura;

/// <summary>Ventura EaseAPI order condition.</summary>
public class VenturaOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Trading product.</summary>
	[Display(
		Name = "Product",
		Description = "Ventura trading product.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public VenturaProducts Product { get; set; } =
		VenturaProducts.CashAndCarry;

	/// <summary>Stop trigger price.</summary>
	[Display(
		Name = "Trigger price",
		Description = "Stop-loss trigger price.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public decimal? TriggerPrice { get; set; }

	/// <summary>Quantity disclosed to the exchange.</summary>
	[Display(
		Name = "Disclosed quantity",
		Description = "Quantity disclosed to the exchange.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public decimal? DisclosedVolume { get; set; }

	/// <summary>Whether the order is submitted outside the regular session.</summary>
	[Display(
		Name = "After-market order",
		Description = "Set the EaseAPI off-market flag for the order.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public bool AfterMarket { get; set; }

	/// <summary>Optional remarks sent when the order is modified.</summary>
	[Display(
		Name = "Remarks",
		Description = "Optional remarks sent with an order modification.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 4)]
	public string Remarks { get; set; }
}
