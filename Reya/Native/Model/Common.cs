namespace StockSharp.Reya.Native.Model;

/// <summary>Reya execution side.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaSides
{
	[EnumMember(Value = "B")]
	Buy,
	[EnumMember(Value = "A")]
	Sell,
}

/// <summary>Reya API order kinds.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,
	[EnumMember(Value = "TP")]
	TakeProfit,
	[EnumMember(Value = "SL")]
	StopLoss,
}

/// <summary>Reya order states.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaOrderStates
{
	[EnumMember(Value = "OPEN")]
	Open,
	[EnumMember(Value = "FILLED")]
	Filled,
	[EnumMember(Value = "CANCELLED")]
	Cancelled,
	[EnumMember(Value = "REJECTED")]
	Rejected,
}

/// <summary>Reya time-in-force values.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaTimeInForces
{
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,
}

/// <summary>Reya execution kinds.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaExecutionTypes
{
	[EnumMember(Value = "ORDER_MATCH")]
	OrderMatch,
	[EnumMember(Value = "LIQUIDATION")]
	Liquidation,
	[EnumMember(Value = "ADL")]
	AutoDeleveraging,
}

/// <summary>Reya order-book message kinds.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaDepthTypes
{
	[EnumMember(Value = "SNAPSHOT")]
	Snapshot,
	[EnumMember(Value = "UPDATE")]
	Update,
}

/// <summary>Reya account kinds.</summary>
[JsonConverter(typeof(StringEnumConverter))]
enum ReyaAccountTypes
{
	[EnumMember(Value = "MAINPERP")]
	MainPerpetual,
	[EnumMember(Value = "SUBPERP")]
	SubPerpetual,
	[EnumMember(Value = "SPOT")]
	Spot,
}

/// <summary>Reya Orders Gateway order kinds.</summary>
enum ReyaGatewayOrderTypes
{
	StopLoss = 0,
	TakeProfit = 1,
	Limit = 2,
	Market = 3,
	ReduceOnlyMarket = 4,
	FullClose = 5,
	SpotLimit = 6,
}

/// <summary>Conditional Reya order kind.</summary>
public enum ReyaTriggerOrderTypes
{
	/// <summary>Stop loss.</summary>
	StopLoss,
	/// <summary>Take profit.</summary>
	TakeProfit,
}

sealed class ReyaApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}

sealed class ReyaMarket
{
	public string Symbol { get; init; }
	public long MarketId { get; init; }
	public string BaseAsset { get; init; }
	public string QuoteAsset { get; init; }
	public bool IsSpot { get; init; }
	public decimal MinimumQuantity { get; init; }
	public decimal QuantityStep { get; init; }
	public decimal PriceStep { get; init; }
	public int? MaximumLeverage { get; init; }
}

sealed class ReyaPriceState
{
	public decimal? OraclePrice { get; set; }
	public decimal? PoolPrice { get; set; }
	public decimal? Volume24Hours { get; set; }
	public decimal? PriceChange24Hours { get; set; }
	public decimal? OpenInterest { get; set; }
	public decimal? FundingRate { get; set; }
	public DateTime UpdatedAt { get; set; }
}
