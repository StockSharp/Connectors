namespace StockSharp.AltCoinTrader.Native.Model;

sealed class AltCoinTraderError
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class AltCoinTraderWsFrame
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("subscription")]
	public string Subscription { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}
