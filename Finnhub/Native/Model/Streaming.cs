namespace StockSharp.Finnhub.Native.Model;

sealed class FinnhubStreamRequest
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class FinnhubStreamEnvelope
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("data")]
	public FinnhubStreamTrade[] Data { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class FinnhubStreamTrade
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("t")]
	public long? Timestamp { get; set; }

	[JsonProperty("v")]
	public decimal? Volume { get; set; }

	[JsonProperty("c")]
	public string[] Conditions { get; set; }
}
