namespace StockSharp.Backpack.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackMarketTypes
{
	[EnumMember(Value = "SPOT")]
	Spot,

	[EnumMember(Value = "PERP")]
	Perpetual,

	[EnumMember(Value = "IPERP")]
	InversePerpetual,

	[EnumMember(Value = "DATED")]
	Dated,

	[EnumMember(Value = "PREDICTION")]
	Prediction,

	[EnumMember(Value = "RFQ")]
	RequestForQuote,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackSides
{
	[EnumMember(Value = "Bid")]
	Bid,

	[EnumMember(Value = "Ask")]
	Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackOrderTypes
{
	[EnumMember(Value = "Market")]
	Market,

	[EnumMember(Value = "Limit")]
	Limit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackOrderStatuses
{
	[EnumMember(Value = "New")]
	New,

	[EnumMember(Value = "PartiallyFilled")]
	PartiallyFilled,

	[EnumMember(Value = "Filled")]
	Filled,

	[EnumMember(Value = "Cancelled")]
	Cancelled,

	[EnumMember(Value = "Expired")]
	Expired,

	[EnumMember(Value = "TriggerPending")]
	TriggerPending,

	[EnumMember(Value = "TriggerFailed")]
	TriggerFailed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackSelfTradePreventions
{
	[EnumMember(Value = "RejectTaker")]
	RejectTaker,

	[EnumMember(Value = "RejectMaker")]
	RejectMaker,

	[EnumMember(Value = "RejectBoth")]
	RejectBoth,
}

readonly record struct BackpackParameter(string Name, string Value);

interface IBackpackParameters
{
	BackpackParameter[] GetParameters();
}

sealed class BackpackEmptyParameters : IBackpackParameters
{
	public static BackpackEmptyParameters Instance { get; } = new();

	private BackpackEmptyParameters()
	{
	}

	public BackpackParameter[] GetParameters() => [];
}

sealed class BackpackError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
