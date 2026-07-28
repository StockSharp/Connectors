namespace StockSharp.Coincall;

/// <summary>
/// Coincall derivatives product.
/// </summary>
public enum CoincallProductTypes
{
	/// <summary>
	/// Crypto options.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OptionsKey)]
	Options,

	/// <summary>
	/// Futures and perpetual contracts.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	Futures,
}
