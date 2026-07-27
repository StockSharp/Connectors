namespace StockSharp.HdfcSecurities;

/// <summary>HDFC Securities order condition.</summary>
public class HdfcOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Trading product.</summary>
	[Display(
		Name = "Product",
		Description = "HDFC Securities trading product.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public HdfcProducts Product { get; set; } = HdfcProducts.Delivery;

	/// <summary>Stop trigger price.</summary>
	[Display(
		Name = "Trigger price",
		Description = "Stop-loss trigger price.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public decimal? TriggerPrice { get; set; }

	/// <summary>Whether the order is an after-market order.</summary>
	[Display(
		Name = "After-market order",
		Description = "Submit the order for the next market session.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public bool AfterMarket { get; set; }

	/// <summary>Optional numeric client reference.</summary>
	[Display(
		Name = "External reference",
		Description = "Optional numeric client reference with at most 20 digits.",
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public long? ExternalReferenceNumber { get; set; }
}
