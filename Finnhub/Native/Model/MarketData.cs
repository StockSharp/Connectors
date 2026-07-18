namespace StockSharp.Finnhub.Native.Model;

sealed class FinnhubQuote
{
	[JsonProperty("o")]
	public decimal? Open { get; set; }

	[JsonProperty("h")]
	public decimal? High { get; set; }

	[JsonProperty("l")]
	public decimal? Low { get; set; }

	[JsonProperty("c")]
	public decimal? Current { get; set; }

	[JsonProperty("pc")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("d")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("dp")]
	public decimal? PercentChange { get; set; }

	[JsonProperty("t")]
	public long? Timestamp { get; set; }
}

sealed class FinnhubBidAsk
{
	[JsonProperty("b")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("a")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bv")]
	public decimal? BidVolume { get; set; }

	[JsonProperty("av")]
	public decimal? AskVolume { get; set; }

	[JsonProperty("t")]
	public long? Timestamp { get; set; }
}

sealed class FinnhubCandles
{
	[JsonProperty("o")]
	public decimal[] Open { get; set; }

	[JsonProperty("h")]
	public decimal[] High { get; set; }

	[JsonProperty("l")]
	public decimal[] Low { get; set; }

	[JsonProperty("c")]
	public decimal[] Close { get; set; }

	[JsonProperty("v")]
	public decimal[] Volume { get; set; }

	[JsonProperty("t")]
	public long[] Timestamp { get; set; }

	[JsonProperty("s")]
	public string Status { get; set; }
}

sealed class FinnhubTicks
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("skip")]
	public long? Skip { get; set; }

	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("total")]
	public long? Total { get; set; }

	[JsonProperty("v")]
	public decimal[] Volume { get; set; }

	[JsonProperty("p")]
	public decimal[] Price { get; set; }

	[JsonProperty("t")]
	public long[] Timestamp { get; set; }

	[JsonProperty("x")]
	public string[] Venue { get; set; }

	[JsonProperty("c")]
	public string[][] Conditions { get; set; }
}

sealed class FinnhubNewsItem
{
	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("datetime")]
	public long? Timestamp { get; set; }

	[JsonProperty("headline")]
	public string Headline { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("image")]
	public string Image { get; set; }

	[JsonProperty("related")]
	public string Related { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("summary")]
	public string Summary { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }
}

sealed class FinnhubErrorResponse
{
	[JsonProperty("error")]
	public string Error { get; set; }
}
