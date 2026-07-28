namespace StockSharp.CoinCatch;

/// <summary>
/// CoinCatch market product.
/// </summary>
[DataContract]
[Serializable]
public enum CoinCatchProductTypes
{
	/// <summary>
	/// Spot market.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey)]
	Spot,

	/// <summary>
	/// USDT-margined futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	UsdtFutures,

	/// <summary>
	/// Coin-margined futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey)]
	CoinFutures,
}
