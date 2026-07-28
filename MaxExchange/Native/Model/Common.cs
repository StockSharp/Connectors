namespace StockSharp.MaxExchange.Native.Model;

sealed class MaxExchangeError
{
	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("errors")]
	public string[] Errors { get; set; }
}
