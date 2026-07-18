namespace StockSharp.Tiingo.Native.Model;

sealed class TiingoIexQuote
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("quoteTimestamp")]
	public string QuoteTimestamp { get; set; }

	[JsonProperty("lastSaleTimestamp")]
	public string LastSaleTimestamp { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("lastSize")]
	public decimal? LastSize { get; set; }

	[JsonProperty("tngoLast")]
	public decimal? TiingoLast { get; set; }

	[JsonProperty("prevClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("mid")]
	public decimal? Mid { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }
}

sealed class TiingoFxQuote
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("quoteTimestamp")]
	public string QuoteTimestamp { get; set; }

	[JsonProperty("midPrice")]
	public decimal? MidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }
}

sealed class TiingoCandle
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

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

	[JsonProperty("adjOpen")]
	public decimal? AdjustedOpen { get; set; }

	[JsonProperty("adjHigh")]
	public decimal? AdjustedHigh { get; set; }

	[JsonProperty("adjLow")]
	public decimal? AdjustedLow { get; set; }

	[JsonProperty("adjClose")]
	public decimal? AdjustedClose { get; set; }

	[JsonProperty("adjVolume")]
	public decimal? AdjustedVolume { get; set; }

	[JsonProperty("divCash")]
	public decimal? DividendCash { get; set; }

	[JsonProperty("splitFactor")]
	public decimal? SplitFactor { get; set; }

	[JsonProperty("tradesDone")]
	public long? TradesDone { get; set; }

	[JsonProperty("volumeNotional")]
	public decimal? VolumeNotional { get; set; }
}

sealed class TiingoCryptoPrices
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("priceData")]
	public TiingoCandle[] PriceData { get; set; }
}

sealed class TiingoNewsItem
{
	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("publishedDate")]
	public string PublishedDate { get; set; }

	[JsonProperty("crawlDate")]
	public string CrawlDate { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; }

	[JsonProperty("tickers")]
	public string[] Tickers { get; set; }

	[JsonProperty("tags")]
	public string[] Tags { get; set; }
}
