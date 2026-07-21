namespace StockSharp.Kalshi;

/// <summary>Kalshi self-trade prevention modes.</summary>
[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
public enum KalshiSelfTradePreventionTypes
{
	/// <summary>Cancel the incoming taker when it would cross the user's order.</summary>
	[EnumMember(Value = "taker_at_cross")]
	[Display(Name = "Cancel taker")]
	TakerAtCross,

	/// <summary>Cancel the resting maker and continue matching.</summary>
	[EnumMember(Value = "maker")]
	[Display(Name = "Cancel maker")]
	Maker,
}
