namespace StockSharp.Luno.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum LunoTradingStatuses
{
	[EnumMember(Value = "POST_ONLY")]
	PostOnly,

	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "SUSPENDED")]
	Suspended,

	[EnumMember(Value = "UNKNOWN")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoTickerStatuses
{
	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "POSTONLY")]
	PostOnly,

	[EnumMember(Value = "DISABLED")]
	Disabled,

	[EnumMember(Value = "UNKNOWN")]
	Unknown,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoStreamStatuses
{
	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "POSTONLY")]
	PostOnly,

	[EnumMember(Value = "DISABLED")]
	Disabled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoLimitSides
{
	[EnumMember(Value = "BID")]
	Bid,

	[EnumMember(Value = "ASK")]
	Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoTimeInForce
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoOrderStatuses
{
	[EnumMember(Value = "AWAITING")]
	Awaiting,

	[EnumMember(Value = "PENDING")]
	Pending,

	[EnumMember(Value = "COMPLETE")]
	Complete,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "STOP_LIMIT")]
	StopLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum LunoStopDirections
{
	[EnumMember(Value = "ABOVE")]
	Above,

	[EnumMember(Value = "BELOW")]
	Below,

	[EnumMember(Value = "RELATIVE_LAST_TRADE")]
	RelativeLastTrade,
}

sealed class LunoIdResponse
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }
}

sealed class LunoSuccessResponse
{
	[JsonProperty("success")]
	public bool IsSuccess { get; init; }
}

sealed class LunoErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("error_code")]
	public string ErrorCode { get; init; }
}

sealed class LunoApiException : InvalidOperationException
{
	public LunoApiException(HttpStatusCode statusCode, string errorCode,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorCode = errorCode;
	}

	public HttpStatusCode StatusCode { get; }
	public string ErrorCode { get; }
}
