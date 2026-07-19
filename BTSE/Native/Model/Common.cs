namespace StockSharp.BTSE.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BTSESides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTSEOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTSETimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTSETransactionTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "STOP")]
	Stop,

	[EnumMember(Value = "TRIGGER")]
	Trigger,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BTSEAmendTypes
{
	[EnumMember(Value = "ALL")]
	All,
}

readonly record struct BTSEParameter(string Name, string Value);

interface IBTSEQuery
{
	BTSEParameter[] GetParameters();
}

sealed class BTSEEmptyQuery : IBTSEQuery
{
	public static BTSEEmptyQuery Instance { get; } = new();

	private BTSEEmptyQuery()
	{
	}

	public BTSEParameter[] GetParameters() => [];
}

sealed class BTSEError
{
	[JsonProperty("status")]
	public int? Status { get; set; }

	[JsonProperty("errorCode")]
	public long? ErrorCode { get; set; }

	[JsonProperty("code")]
	public long? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	public string ShortMessage { get; set; }
}
