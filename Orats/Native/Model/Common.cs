namespace StockSharp.Orats.Native.Model;

sealed class OratsResponse<T>
{
	[JsonProperty("data")]
	public T[] Data { get; set; }
}

sealed class OratsTicker
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("min")]
	public string MinimumDate { get; set; }

	[JsonProperty("max")]
	public string MaximumDate { get; set; }
}

sealed class OratsErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }
}
