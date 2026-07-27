namespace StockSharp.HdfcSecurities;

/// <summary>HDFC Securities order condition.</summary>
public class HdfcOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Trading product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.HdfcSecuritiesTradingProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public HdfcProducts Product { get; set; } = HdfcProducts.Delivery;

	/// <summary>Stop trigger price.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerPriceKey,
		Description = LocalizedStrings.StopLossTriggerPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public decimal? TriggerPrice { get; set; }

	/// <summary>Whether the order is an after-market order.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AfterMarketOrderKey,
		Description = LocalizedStrings.SubmitTheOrderForTheNextMarketSessionDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public bool AfterMarket { get; set; }

	/// <summary>Optional numeric client reference.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExternalReferenceKey,
		Description = LocalizedStrings.OptionalNumericClientReferenceWithAtMost20DigitsDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public long? ExternalReferenceNumber { get; set; }
}
