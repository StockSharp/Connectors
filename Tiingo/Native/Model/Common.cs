namespace StockSharp.Tiingo.Native.Model;

sealed class TiingoTestResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class TiingoErrorResponse
{
	[JsonProperty("detail")]
	public string Detail { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }
}
