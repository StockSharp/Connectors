namespace StockSharp.Ventura;

/// <summary>Ventura EaseAPI order condition.</summary>
public class VenturaOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>Trading product.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ProductKey,
		Description = LocalizedStrings.VenturaTradingProductDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	public VenturaProducts Product { get; set; } =
		VenturaProducts.CashAndCarry;

	/// <summary>Stop trigger price.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerPriceKey,
		Description = LocalizedStrings.StopLossTriggerPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	public decimal? TriggerPrice { get; set; }

	/// <summary>Quantity disclosed to the exchange.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DisclosedQuantityKey,
		Description = LocalizedStrings.QuantityDisclosedToTheExchangeDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	public decimal? DisclosedVolume { get; set; }

	/// <summary>Whether the order is submitted outside the regular session.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AfterMarketOrderKey,
		Description = LocalizedStrings.SetTheEaseAPIOffMarketFlagForTheOrderDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 3)]
	public bool AfterMarket { get; set; }

	/// <summary>Optional remarks sent when the order is modified.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RemarksKey,
		Description = LocalizedStrings.OptionalRemarksSentWithAnOrderModificationDescKey,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 4)]
	public string Remarks { get; set; }
}
