namespace StockSharp.Marketstack.Native.Model;

sealed class MarketstackTicker
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("has_intraday")]
	public bool? HasIntraday { get; set; }

	[JsonProperty("has_eod")]
	public bool? HasEod { get; set; }

	[JsonProperty("stock_exchange")]
	public MarketstackExchangeSummary StockExchange { get; set; }
}

sealed class MarketstackExchangeSummary
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("acronym")]
	public string Acronym { get; set; }

	[JsonProperty("mic")]
	public string Mic { get; set; }
}
