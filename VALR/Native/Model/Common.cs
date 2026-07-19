namespace StockSharp.VALR.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum VALRSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRTimeInForce
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRPairTypes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "FUTURE")]
	Future,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRConditionalTypes
{
	[EnumMember(Value = "STOP_LOSS_LIMIT")]
	StopLossLimit,

	[EnumMember(Value = "TAKE_PROFIT_LIMIT")]
	TakeProfitLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRModifyMatchStrategies
{
	[EnumMember(Value = "RETAIN_ORIGINAL")]
	RetainOriginal,

	[EnumMember(Value = "CANCEL_ORIGINAL")]
	CancelOriginal,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALROrderTypes
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "post-only limit")]
	PostOnlyLimit,

	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "stop-loss-limit")]
	StopLossLimit,

	[EnumMember(Value = "take-profit-limit")]
	TakeProfitLimit,

	[EnumMember(Value = "limit-reduce-only")]
	LimitReduceOnly,

	[EnumMember(Value = "simple")]
	Simple,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALROrderStatuses
{
	[EnumMember(Value = "Placed")]
	Placed,

	[EnumMember(Value = "Active")]
	Active,

	[EnumMember(Value = "Partially Filled")]
	PartiallyFilled,

	[EnumMember(Value = "Partially Filled Due To Slippage")]
	PartiallyFilledDueToSlippage,

	[EnumMember(Value = "Order Modified")]
	OrderModified,

	[EnumMember(Value = "Filled")]
	Filled,

	[EnumMember(Value = "Cancelled")]
	Cancelled,

	[EnumMember(Value = "Expired")]
	Expired,

	[EnumMember(Value = "Failed")]
	Failed,
}

sealed class VALRIdResponse
{
	[JsonProperty("id")]
	public string Id { get; init; }
}

sealed class VALRErrorResponse
{
	[JsonProperty("code")]
	public int? Code { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }
}

sealed class VALRApiException : InvalidOperationException
{
	public VALRApiException(HttpStatusCode statusCode, int? code,
		string orderId, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
		OrderId = orderId;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
	public string OrderId { get; }
}
