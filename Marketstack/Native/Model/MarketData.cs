namespace StockSharp.Marketstack.Native.Model;

sealed class MarketstackBar
{
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

	[JsonProperty("adj_open")]
	public decimal? AdjustedOpen { get; set; }

	[JsonProperty("adj_high")]
	public decimal? AdjustedHigh { get; set; }

	[JsonProperty("adj_low")]
	public decimal? AdjustedLow { get; set; }

	[JsonProperty("adj_close")]
	public decimal? AdjustedClose { get; set; }

	[JsonProperty("adj_volume")]
	public decimal? AdjustedVolume { get; set; }

	[JsonProperty("split_factor")]
	public decimal? SplitFactor { get; set; }

	[JsonProperty("dividend")]
	public decimal? Dividend { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("asset_type")]
	public string AssetType { get; set; }

	[JsonProperty("price_currency")]
	public string PriceCurrency { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("date")]
	public string Date { get; set; }
}

sealed class MarketstackStockPriceResponse
{
	[JsonProperty("data")]
	public MarketstackStockPrice[] Data { get; set; }
}

sealed class MarketstackStockPrice
{
	[JsonProperty("exchange_code")]
	public string ExchangeCode { get; set; }

	[JsonProperty("exchange_name")]
	public string ExchangeName { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("trade_last")]
	public string TradeLast { get; set; }
}
