namespace StockSharp.Gemini.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiTradeSides
{
	[EnumMember(Value = "Buy")]
	Buy,

	[EnumMember(Value = "Sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiWsSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiSymbolStatuses
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "closed")]
	Closed,

	[EnumMember(Value = "cancel_only")]
	CancelOnly,

	[EnumMember(Value = "post_only")]
	PostOnly,

	[EnumMember(Value = "limit_only")]
	LimitOnly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiProductTypes
{
	[EnumMember(Value = "spot")]
	Spot,

	[EnumMember(Value = "swap")]
	Swap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiContractTypes
{
	[EnumMember(Value = "vanilla")]
	Vanilla,

	[EnumMember(Value = "linear")]
	Linear,

	[EnumMember(Value = "inverse")]
	Inverse,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiRestOrderTypes
{
	[EnumMember(Value = "exchange limit")]
	Limit,

	[EnumMember(Value = "exchange stop limit")]
	StopLimit,

	[EnumMember(Value = "exchange market")]
	Market,

	[EnumMember(Value = "exchange stop market")]
	StopMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiOrderOptions
{
	[EnumMember(Value = "maker-or-cancel")]
	MakerOrCancel,

	[EnumMember(Value = "immediate-or-cancel")]
	ImmediateOrCancel,

	[EnumMember(Value = "fill-or-kill")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiWsOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,

	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiWsTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "MOC")]
	MakerOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiWsOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "OPEN")]
	Open,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "PARTIALLY_FILLED")]
	PartiallyFilled,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "MODIFIED")]
	Modified,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GeminiWsMethods
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "ping")]
	Ping,

	[EnumMember(Value = "order.place")]
	PlaceOrder,

	[EnumMember(Value = "order.cancel")]
	CancelOrder,

	[EnumMember(Value = "order.cancel_all")]
	CancelAllOrders,

	[EnumMember(Value = "order.cancel_session")]
	CancelSessionOrders,
}

abstract class GeminiPrivateRequest
{
	[JsonProperty("request")]
	public string Request { get; set; }

	[JsonProperty("nonce")]
	public string Nonce { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }
}

sealed class GeminiEmptyPrivateRequest : GeminiPrivateRequest
{
}

sealed class GeminiError
{
	[JsonProperty("result")]
	public string Result { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
