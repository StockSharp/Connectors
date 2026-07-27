namespace StockSharp.Bit2Me.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum Bit2MeSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum Bit2MeOrderTypes
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "stop-limit")]
	StopLimit,

	[EnumMember(Value = "market")]
	Market,
}

[JsonConverter(typeof(StringEnumConverter))]
enum Bit2MeOrderStatuses
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "cancelled")]
	Cancelled,

	[EnumMember(Value = "inactive")]
	Inactive,
}

[JsonConverter(typeof(StringEnumConverter))]
enum Bit2MeTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum Bit2MeMarketStatuses
{
	[EnumMember(Value = "enabled")]
	Enabled,

	[EnumMember(Value = "enabled_at")]
	EnabledAt,

	[EnumMember(Value = "frozen")]
	Frozen,

	[EnumMember(Value = "disabled")]
	Disabled,
}

readonly record struct Bit2MeParameter(string Name, string Value);

interface IBit2MeQuery
{
	Bit2MeParameter[] GetParameters();
}

sealed class Bit2MeEmptyQuery : IBit2MeQuery
{
	public static Bit2MeEmptyQuery Instance { get; } = new();

	private Bit2MeEmptyQuery()
	{
	}

	public Bit2MeParameter[] GetParameters() => [];
}

sealed class Bit2MeError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}
