namespace StockSharp.BtcTurk.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BtcTurkSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BtcTurkOrderMethods
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "stoplimit")]
	StopLimit,

	[EnumMember(Value = "stopmarket")]
	StopMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BtcTurkOrderStatuses
{
	[EnumMember(Value = "Untouched")]
	Untouched,

	[EnumMember(Value = "Partial")]
	Partial,

	[EnumMember(Value = "Closed")]
	Closed,

	[EnumMember(Value = "Canceled")]
	Canceled,

	[EnumMember(Value = "Expired")]
	Expired,

	[EnumMember(Value = "Rejected")]
	Rejected,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BtcTurkMarketStatuses
{
	[EnumMember(Value = "TRADING")]
	Trading,

	[EnumMember(Value = "BREAK")]
	Break,

	[EnumMember(Value = "HALT")]
	Halt,
}

enum BtcTurkWsMessageTypes
{
	Result = 100,
	Subscription = 151,
	Ticker = 402,
	TradeHistory = 421,
	Trade = 422,
	OrderBook = 431,
	OrderBookDifference = 432,
	Version = 991,
}

readonly record struct BtcTurkParameter(string Name, string Value);

interface IBtcTurkQuery
{
	BtcTurkParameter[] GetParameters();
}

sealed class BtcTurkEmptyQuery : IBtcTurkQuery
{
	public static BtcTurkEmptyQuery Instance { get; } = new();

	private BtcTurkEmptyQuery()
	{
	}

	public BtcTurkParameter[] GetParameters() => [];
}

sealed class BtcTurkResponse<TData>
{
	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public JToken Code { get; set; }

	[JsonProperty("details")]
	public string Details { get; set; }
}
