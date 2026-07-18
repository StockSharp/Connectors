namespace StockSharp.NasdaqCloudDataService.Native.Model;

class NasdaqCloudOptionChainItem : NasdaqCloudOptionContract
{
	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("askPrice")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("askSize")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bidPrice")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("bidSize")]
	public decimal? BidSize { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }
}

sealed class NasdaqCloudOptionPrice : NasdaqCloudOptionChainItem
{
	[JsonProperty("lastSize")]
	public decimal? LastSize { get; set; }

	[JsonProperty("tradeTimestamp")]
	public string TradeTimestamp { get; set; }

	[JsonProperty("askTimestamp")]
	public string AskTimestamp { get; set; }

	[JsonProperty("bidTimestamp")]
	public string BidTimestamp { get; set; }
}

sealed class NasdaqCloudOptionGreeks
{
	[JsonProperty("u")]
	public string Underlying { get; set; }

	[JsonProperty("d")]
	public decimal? Delta { get; set; }

	[JsonProperty("g")]
	public decimal? Gamma { get; set; }

	[JsonProperty("vg")]
	public decimal? Vega { get; set; }

	[JsonProperty("tt")]
	public decimal? Theta { get; set; }

	[JsonProperty("r")]
	public decimal? Rho { get; set; }

	[JsonProperty("iv")]
	public decimal? ImpliedVolatility { get; set; }

	[JsonProperty("tp")]
	public decimal? TheoreticalPrice { get; set; }

	[JsonProperty("t")]
	public string Timestamp { get; set; }

	[JsonProperty("i")]
	public string Identifier { get; set; }
}
