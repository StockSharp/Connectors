namespace StockSharp.FinancialModelingPrep.Native.Model;

sealed class FmpQuote
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("changePercentage")]
	public decimal? ChangePercentage { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("averageVolume")]
	public decimal? AverageVolume { get; set; }

	[JsonProperty("dayLow")]
	public decimal? DayLow { get; set; }

	[JsonProperty("dayHigh")]
	public decimal? DayHigh { get; set; }

	[JsonProperty("yearHigh")]
	public decimal? YearHigh { get; set; }

	[JsonProperty("yearLow")]
	public decimal? YearLow { get; set; }

	[JsonProperty("marketCap")]
	public decimal? MarketCap { get; set; }

	[JsonProperty("priceAvg50")]
	public decimal? PriceAverage50 { get; set; }

	[JsonProperty("priceAvg200")]
	public decimal? PriceAverage200 { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }
}

sealed class FmpBar
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

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

	[JsonProperty("adjOpen")]
	public decimal? AdjustedOpen { get; set; }

	[JsonProperty("adjHigh")]
	public decimal? AdjustedHigh { get; set; }

	[JsonProperty("adjLow")]
	public decimal? AdjustedLow { get; set; }

	[JsonProperty("adjClose")]
	public decimal? AdjustedClose { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("change")]
	public decimal? Change { get; set; }

	[JsonProperty("changePercent")]
	public decimal? ChangePercent { get; set; }

	[JsonProperty("vwap")]
	public decimal? Vwap { get; set; }
}

sealed class FmpNewsItem
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("publishedDate")]
	public string PublishedDate { get; set; }

	[JsonProperty("publisher")]
	public string Publisher { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("image")]
	public string Image { get; set; }

	[JsonProperty("site")]
	public string Site { get; set; }

	[JsonProperty("text")]
	public string Text { get; set; }

	[JsonProperty("url")]
	public string Url { get; set; }
}
