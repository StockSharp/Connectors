namespace StockSharp.Marketstack.Native.Model;

sealed class MarketstackPagination
{
	[JsonProperty("limit")]
	public long Limit { get; set; }

	[JsonProperty("offset")]
	public long Offset { get; set; }

	[JsonProperty("count")]
	public long Count { get; set; }

	[JsonProperty("total")]
	public long Total { get; set; }
}

sealed class MarketstackPage<T>
{
	[JsonProperty("pagination")]
	public MarketstackPagination Pagination { get; set; }

	[JsonProperty("data")]
	public T[] Data { get; set; }
}

sealed class MarketstackErrorEnvelope
{
	[JsonProperty("error")]
	public MarketstackError Error { get; set; }
}

sealed class MarketstackError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
