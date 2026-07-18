namespace StockSharp.TwelveData.Native.Model;

sealed class TwelveDataQuote : TwelveDataResponse
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("mic_code")]
	public string MicCode { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("datetime")]
	public string DateTime { get; set; }

	[JsonProperty("timestamp")]
	public long? Timestamp { get; set; }

	[JsonProperty("last_quote_at")]
	public long? LastQuoteAt { get; set; }

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

	[JsonProperty("previous_close")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("change")]
	public decimal? PriceChange { get; set; }

	[JsonProperty("percent_change")]
	public decimal? PercentChange { get; set; }

	[JsonProperty("average_volume")]
	public decimal? AverageVolume { get; set; }

	[JsonProperty("is_market_open")]
	public bool? IsMarketOpen { get; set; }

	[JsonProperty("fifty_two_week")]
	public TwelveDataFiftyTwoWeek FiftyTwoWeek { get; set; }

	[JsonProperty("extended_change")]
	public decimal? ExtendedChange { get; set; }

	[JsonProperty("extended_percent_change")]
	public decimal? ExtendedPercentChange { get; set; }

	[JsonProperty("extended_price")]
	public decimal? ExtendedPrice { get; set; }

	[JsonProperty("extended_timestamp")]
	public long? ExtendedTimestamp { get; set; }
}

sealed class TwelveDataFiftyTwoWeek
{
	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }
}

sealed class TwelveDataTimeSeries : TwelveDataResponse
{
	[JsonProperty("meta")]
	public TwelveDataTimeSeriesMeta Meta { get; set; }

	[JsonProperty("values")]
	public TwelveDataCandle[] Values { get; set; }
}

sealed class TwelveDataTimeSeriesMeta
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("exchange_timezone")]
	public string ExchangeTimezone { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("mic_code")]
	public string MicCode { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

sealed class TwelveDataCandle
{
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
