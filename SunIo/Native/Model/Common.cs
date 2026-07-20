namespace StockSharp.SunIo.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum SunIoProtocols
{
	[EnumMember(Value = "ALL")]
	All,

	[EnumMember(Value = "V1")]
	V1,

	[EnumMember(Value = "V1_5")]
	V1_5,

	[EnumMember(Value = "V2")]
	V2,

	[EnumMember(Value = "V3")]
	V3,

	[EnumMember(Value = "V4")]
	V4,

	[EnumMember(Value = "CURVE")]
	Curve,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SunIoPoolVersions
{
	[EnumMember(Value = "v1")]
	V1,

	[EnumMember(Value = "v2")]
	V2,

	[EnumMember(Value = "v3")]
	V3,

	[EnumMember(Value = "usdt20psm")]
	Usdt20Psm,

	[EnumMember(Value = "usdd202pool")]
	Usdd202Pool,

	[EnumMember(Value = "2pooltusdusdt")]
	TwoPoolTusdUsdt,

	[EnumMember(Value = "usdc2pooltusdusdt")]
	UsdcTwoPoolTusdUsdt,

	[EnumMember(Value = "usdd2pooltusdusdt")]
	UsddTwoPoolTusdUsdt,

	[EnumMember(Value = "usdj2pooltusdusdt")]
	UsdjTwoPoolTusdUsdt,

	[EnumMember(Value = "oldusdcpool")]
	OldUsdcPool,

	[EnumMember(Value = "old3pool")]
	OldThreePool,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SunIoTransactionTypes
{
	[EnumMember(Value = "swap")]
	Swap,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SunIoContractTypes
{
	[EnumMember(Value = "TriggerSmartContract")]
	TriggerSmartContract,
}

[JsonConverter(typeof(StringEnumConverter))]
enum SunIoReceiptResults
{
	[EnumMember(Value = "SUCCESS")]
	Success,

	[EnumMember(Value = "FAILED")]
	Failed,

	[EnumMember(Value = "REVERT")]
	Revert,

	[EnumMember(Value = "OUT_OF_ENERGY")]
	OutOfEnergy,

	[EnumMember(Value = "OUT_OF_TIME")]
	OutOfTime,

	[EnumMember(Value = "BAD_JUMP_DESTINATION")]
	BadJumpDestination,

	[EnumMember(Value = "OUT_OF_MEMORY")]
	OutOfMemory,

	[EnumMember(Value = "STACK_TOO_SMALL")]
	StackTooSmall,

	[EnumMember(Value = "STACK_TOO_LARGE")]
	StackTooLarge,

	[EnumMember(Value = "ILLEGAL_OPERATION")]
	IllegalOperation,
}

sealed class SunIoMarket
{
	public SunIoToken Token { get; set; }
	public string SecurityCode { get; init; }
	public string BalanceCode { get; init; }
}

sealed class SunIoMarketDefinition
{
	public string TokenAddress { get; init; }
	public string SecurityCode { get; init; }
}

sealed class SunIoTrade
{
	public string Id { get; init; }
	public string TransactionHash { get; init; }
	public DateTime Time { get; init; }
	public decimal Price { get; init; }
	public decimal Volume { get; init; }
	public decimal QuoteVolume { get; init; }
	public Sides Side { get; init; }
	public string UserAddress { get; init; }
}

sealed class SunIoCandle
{
	public DateTime OpenTime { get; init; }
	public decimal Open { get; init; }
	public decimal High { get; init; }
	public decimal Low { get; init; }
	public decimal Close { get; init; }
	public decimal Volume { get; init; }
	public decimal Turnover { get; init; }
	public int TradeCount { get; init; }
}

sealed class SunIoApiException : InvalidOperationException
{
	public SunIoApiException(HttpStatusCode statusCode, int? code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public SunIoApiException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }
}
