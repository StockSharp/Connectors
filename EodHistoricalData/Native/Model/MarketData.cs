namespace StockSharp.EodHistoricalData.Native.Model;

sealed class EodhdEodBar
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("adjusted_close")]
	public decimal? AdjustedClose { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }
}

sealed class EodhdIntradayBar
{
	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("gmtoffset")]
	public int? GmtOffset { get; set; }

	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }
}

sealed class EodhdRealTimeQuote
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("gmtoffset")]
	public int? GmtOffset { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("change_p")]
	public decimal? ChangePercent { get; set; }
}

sealed class EodhdTicks
{
	[JsonProperty("mkt")]
	public string[] Markets { get; set; }

	[JsonProperty("price")]
	public decimal[] Prices { get; set; }

	[JsonProperty("seq")]
	public long[] Sequences { get; set; }

	[JsonProperty("shares")]
	public decimal[] Shares { get; set; }

	[JsonProperty("sl")]
	public string[] SaleConditions { get; set; }

	[JsonProperty("sub_mkt")]
	public string[] SubMarkets { get; set; }

	[JsonProperty("ex")]
	public string[] Exchanges { get; set; }

	[JsonProperty("ts")]
	public long[] Timestamps { get; set; }
}

sealed class EodhdNewsItem
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("content")]
	public string Content { get; set; }

	[JsonProperty("link")]
	public string Link { get; set; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("tags")]
	public string[] Tags { get; set; }

	[JsonProperty("sentiment")]
	public EodhdSentiment Sentiment { get; set; }
}

sealed class EodhdSentiment
{
	[JsonProperty("polarity")]
	public decimal? Polarity { get; set; }

	[JsonProperty("neg")]
	public decimal? Negative { get; set; }

	[JsonProperty("neu")]
	public decimal? Neutral { get; set; }

	[JsonProperty("pos")]
	public decimal? Positive { get; set; }
}
