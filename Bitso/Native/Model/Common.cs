namespace StockSharp.Bitso.Native.Model;

enum BitsoSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

enum BitsoOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,
}

enum BitsoTimeInForces
{
	[EnumMember(Value = "goodtillcancelled")]
	GoodTillCancelled,

	[EnumMember(Value = "fillorkill")]
	FillOrKill,

	[EnumMember(Value = "immediateorcancel")]
	ImmediateOrCancel,

	[EnumMember(Value = "postonly")]
	PostOnly,
}

enum BitsoOrderStatuses
{
	[EnumMember(Value = "queued")]
	Queued,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "partially filled")]
	PartiallyFilled,

	[EnumMember(Value = "completed")]
	Completed,

	[EnumMember(Value = "cancelled")]
	Cancelled,
}

sealed class BitsoResponse<TPayload>
{
	[JsonProperty("success")]
	public bool? IsSuccess { get; set; }

	[JsonProperty("payload")]
	public TPayload Payload { get; set; }

	[JsonProperty("error")]
	public BitsoError Error { get; set; }
}

sealed class BitsoError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
