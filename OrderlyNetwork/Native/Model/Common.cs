namespace StockSharp.OrderlyNetwork.Native.Model;

sealed class OrderlyNetworkResponse<TData>
{
	[JsonProperty("success")]
	public bool IsSuccess { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("timestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("ts")]
	public long QueryTimestamp { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}

sealed class OrderlyNetworkRows<TItem>
{
	[JsonProperty("rows")]
	public TItem[] Rows { get; set; }

	[JsonProperty("meta")]
	public OrderlyNetworkPagination Pagination { get; set; }
}

sealed class OrderlyNetworkPagination
{
	[JsonProperty("total")]
	public int Total { get; set; }

	[JsonProperty("records_per_page")]
	public int PageSize { get; set; }

	[JsonProperty("current_page")]
	public int CurrentPage { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum OrderlyNetworkSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OrderlyNetworkOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "IOC")]
	Ioc,

	[EnumMember(Value = "FOK")]
	Fok,

	[EnumMember(Value = "POST_ONLY")]
	PostOnly,

	[EnumMember(Value = "ASK")]
	Ask,

	[EnumMember(Value = "BID")]
	Bid,
}

[JsonConverter(typeof(StringEnumConverter))]
enum OrderlyNetworkOrderStatuses
{
	[EnumMember(Value = "NEW")]
	New,

	[EnumMember(Value = "PARTIAL_FILLED")]
	PartialFilled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "INCOMPLETE")]
	Incomplete,

	[EnumMember(Value = "COMPLETED")]
	Completed,

	[EnumMember(Value = "PENDING_CANCEL")]
	PendingCancel,

	[EnumMember(Value = "EXPIRED")]
	Expired,
}
