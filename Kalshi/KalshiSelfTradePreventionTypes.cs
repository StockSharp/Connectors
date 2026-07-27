namespace StockSharp.Kalshi;

/// <summary>Kalshi self-trade prevention modes.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum KalshiSelfTradePreventionTypes
{
	/// <summary>Cancel the incoming taker when it would cross the user's order.</summary>
	[EnumMember(Value = "taker_at_cross")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CancelTakerKey)]
	TakerAtCross,

	/// <summary>Cancel the resting maker and continue matching.</summary>
	[EnumMember(Value = "maker")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CancelMakerKey)]
	Maker,
}
