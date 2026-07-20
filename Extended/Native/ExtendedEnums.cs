namespace StockSharp.Extended.Native;

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedMarketTypes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "PERPETUAL")]
	Perpetual,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "CONDITIONAL")]
	Conditional,

	[EnumMember(Value = "TPSL")]
	TakeProfitStopLoss,

	[EnumMember(Value = "TWAP")]
	Twap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedTimeInForces
{
	[EnumMember(Value = "GTT")]
	GoodTillTime,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedOrderStatuses
{
	[EnumMember(Value = "UNKNOWN")]
	Unknown,

	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "UNTRIGGERED")]
	Untriggered,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "EXPIRED")]
	Expired,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedPositionSides
{
	[EnumMember(Value = "LONG")]
	Long,

	[EnumMember(Value = "SHORT")]
	Short,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedPositionStatuses
{
	[EnumMember(Value = "OPENED")]
	Opened,

	[EnumMember(Value = "CLOSED")]
	Closed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedTradeTypes
{
	[EnumMember(Value = "TRADE")]
	Trade,

	[EnumMember(Value = "LIQUIDATION")]
	Liquidation,

	[EnumMember(Value = "DELEVERAGE")]
	Deleverage,
}

[JsonConverter(typeof(StringEnumConverter))]
enum ExtendedSelfTradeProtectionLevels
{
	[EnumMember(Value = "DISABLED")]
	Disabled,

	[EnumMember(Value = "ACCOUNT")]
	Account,

	[EnumMember(Value = "CLIENT")]
	Client,
}

/// <summary>Extended trigger price sources.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ExtendedTriggerPriceTypes
{
	/// <summary>Mark price.</summary>
	[EnumMember(Value = "MARK")]
	Mark,

	/// <summary>Index price.</summary>
	[EnumMember(Value = "INDEX")]
	Index,

	/// <summary>Last traded price.</summary>
	[EnumMember(Value = "LAST")]
	Last,
}

/// <summary>Extended trigger directions.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ExtendedTriggerDirections
{
	/// <summary>Trigger when the reference price rises through the level.</summary>
	[EnumMember(Value = "UP")]
	Up,

	/// <summary>Trigger when the reference price falls through the level.</summary>
	[EnumMember(Value = "DOWN")]
	Down,
}

/// <summary>Extended conditional-order execution price modes.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ExtendedExecutionPriceTypes
{
	/// <summary>Execute as a market order.</summary>
	[EnumMember(Value = "MARKET")]
	Market,

	/// <summary>Execute at the submitted limit price.</summary>
	[EnumMember(Value = "LIMIT")]
	Limit,
}

enum ExtendedStreamScopes
{
	OrderBooks,
	Trades,
	FundingRates,
	Prices,
	Candles,
	Account,
}

enum ExtendedCandleTypes
{
	Last,
	Mark,
	Index,
}

enum ExtendedPriceTypes
{
	Mark,
	Index,
}
