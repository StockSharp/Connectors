namespace StockSharp.Bitkub.Native.Model;

enum BitkubSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

enum BitkubOrderTypes
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "stoplimit")]
	StopLimit,
}

enum BitkubOrderStatuses
{
	[EnumMember(Value = "new")]
	New,

	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "rejected")]
	Rejected,

	[EnumMember(Value = "partial_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "partial_filled_canceled")]
	PartiallyFilledCanceled,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "untriggered")]
	Untriggered,

	[EnumMember(Value = "unfilled")]
	Unfilled,

	[EnumMember(Value = "cancelled")]
	Cancelled,
}

sealed class BitkubResponse<TPayload>
{
	[JsonProperty("error")]
	public int Error { get; set; }

	[JsonProperty("result")]
	public TPayload Result { get; set; }

	[JsonProperty("pagination")]
	public BitkubPagination Pagination { get; set; }
}

sealed class BitkubV4Response<TPayload>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("data")]
	public TPayload Data { get; set; }
}

sealed class BitkubPagination
{
	[JsonProperty("page")]
	public int? Page { get; set; }

	[JsonProperty("last")]
	public int? Last { get; set; }

	[JsonProperty("next")]
	public int? Next { get; set; }

	[JsonProperty("prev")]
	public int? Previous { get; set; }

	[JsonProperty("cursor")]
	public string Cursor { get; set; }

	[JsonProperty("has_next")]
	public bool? HasNext { get; set; }
}

sealed class BitkubApiException : InvalidOperationException
{
	public BitkubApiException(int errorCode, string message)
		: base(message)
	{
		ErrorCode = errorCode;
	}

	public int ErrorCode { get; }
}
