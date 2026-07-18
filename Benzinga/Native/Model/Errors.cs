namespace StockSharp.Benzinga.Native.Model;

sealed class BenzingaErrorResponse
{
	[JsonProperty("ok")]
	public bool? IsOk { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errors")]
	public BenzingaErrorItem[] Errors { get; set; }
}

sealed class BenzingaErrorItem
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("value")]
	public string Value { get; set; }
}
