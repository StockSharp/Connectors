namespace StockSharp.CoinSwitch;

/// <summary>
/// CoinSwitch PRO API product surface.
/// </summary>
[DataContract]
public enum CoinSwitchProductTypes
{
	/// <summary>
	/// INR and USDT spot markets.
	/// </summary>
	[EnumMember]
	Spot,

	/// <summary>
	/// USDT-margined perpetual futures.
	/// </summary>
	[EnumMember]
	Futures,

	/// <summary>
	/// HFT options private-beta surface.
	/// </summary>
	[EnumMember]
	Options,
}
