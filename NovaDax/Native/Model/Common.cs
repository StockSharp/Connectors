namespace StockSharp.NovaDax.Native.Model;

sealed class NovaDaxResponse<TData>
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class NovaDaxError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
