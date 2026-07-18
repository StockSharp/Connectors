namespace StockSharp.EodHistoricalData.Native.Model;

sealed class EodhdStreamRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("symbols")]
	public string Symbols { get; set; }
}

sealed class EodhdStreamMessage
{
	[JsonProperty("status_code")]
	public int? StatusCode { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("q")]
	public decimal? Quantity { get; set; }

	[JsonProperty("c")]
	public int[] Conditions { get; set; }

	[JsonProperty("v")]
	public decimal? Volume { get; set; }

	[JsonProperty("dp")]
	public bool? IsDarkPool { get; set; }

	[JsonProperty("ms")]
	public string MarketStatus { get; set; }

	[JsonProperty("ap")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("as")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bp")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bs")]
	public decimal? BidSize { get; set; }

	[JsonProperty("a")]
	public decimal? Ask { get; set; }

	[JsonProperty("b")]
	public decimal? Bid { get; set; }

	[JsonProperty("dc")]
	public string DailyChange { get; set; }

	[JsonProperty("dd")]
	public string DailyDifference { get; set; }

	[JsonProperty("ppms")]
	public bool? IsPricePerMillisecond { get; set; }

	[JsonProperty("t")]
	public long? Timestamp { get; set; }
}
