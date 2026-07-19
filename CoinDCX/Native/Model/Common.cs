namespace StockSharp.CoinDCX.Native.Model;

enum CoinDCXSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

enum CoinDCXOrderTypes
{
	[EnumMember(Value = "limit_order")]
	LimitOrder,

	[EnumMember(Value = "market_order")]
	MarketOrder,

	[EnumMember(Value = "stop_limit")]
	StopLimit,

	[EnumMember(Value = "take_profit_limit")]
	TakeProfitLimit,

	[EnumMember(Value = "take_profit_market")]
	TakeProfitMarket,
}

enum CoinDCXOrderStatuses
{
	[EnumMember(Value = "init")]
	Init,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "partially_cancelled")]
	PartiallyCancelled,

	[EnumMember(Value = "cancelled")]
	Cancelled,

	[EnumMember(Value = "rejected")]
	Rejected,

	[EnumMember(Value = "untriggered")]
	Untriggered,
}

abstract class CoinDCXPrivateRequest
{
	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }
}

sealed class CoinDCXApiResult
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}
