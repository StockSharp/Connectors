namespace StockSharp.BitpandaFusion.Native.Model;

enum BitpandaFusionPairAssetTypes
{
	[EnumMember(Value = "cryptocoin")]
	Cryptocoin,

	[EnumMember(Value = "fiat")]
	Fiat,
}

enum BitpandaFusionAssetTypes
{
	[EnumMember(Value = "crypto")]
	Crypto,

	[EnumMember(Value = "fiat")]
	Fiat,
}

enum BitpandaFusionOrderSides
{
	[EnumMember(Value = "Buy")]
	Buy,

	[EnumMember(Value = "Sell")]
	Sell,
}

enum BitpandaFusionTradeSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

enum BitpandaFusionOrderTypes
{
	[EnumMember(Value = "Limit")]
	Limit,

	[EnumMember(Value = "Market")]
	Market,

	[EnumMember(Value = "StopLimit")]
	StopLimit,

	[EnumMember(Value = "StopMarket")]
	StopMarket,
}

enum BitpandaFusionOrderStatuses
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "closed")]
	Closed,

	[EnumMember(Value = "new")]
	New,

	[EnumMember(Value = "partially-filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "filled-and-canceled")]
	FilledAndCanceled,

	[EnumMember(Value = "done-for-day")]
	DoneForDay,

	[EnumMember(Value = "rejected")]
	Rejected,
}

enum BitpandaFusionTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "GTD")]
	GoodTillDate,
}

sealed class BitpandaFusionServerTime
{
	[JsonProperty("epochMs")]
	public long EpochMilliseconds { get; set; }

	[JsonProperty("iso")]
	public DateTimeOffset Iso { get; set; }
}

sealed class BitpandaFusionPageMeta
{
	[JsonProperty("limit")]
	public int Limit { get; set; }

	[JsonProperty("hasNextPage")]
	public bool IsNextPageAvailable { get; set; }

	[JsonProperty("nextCursor")]
	public string NextCursor { get; set; }
}

sealed class BitpandaFusionApiError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("status")]
	public int Status { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("field")]
	public string Field { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class BitpandaFusionErrorResponse
{
	[JsonProperty("errors")]
	public BitpandaFusionApiError[] Errors { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}
