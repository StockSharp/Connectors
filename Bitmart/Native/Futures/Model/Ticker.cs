namespace StockSharp.Bitmart.Native.Futures.Model;

class Ticker
{
	// Trading pair, BTC_USDT
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Last trading price
	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	// 24-hour volume in base currency
	[JsonProperty("volume_24")]
	public double? Volume24h { get; set; }

	[JsonProperty("fair_price")]
	public double? FairPrice { get; set; }

	[JsonProperty("ask_price")]
	public double? AskPrice { get; set; }

	[JsonProperty("ask_vol")]
	public double? AskVol { get; set; }

	[JsonProperty("bid_price")]
	public double? BidPrice { get; set; }

	[JsonProperty("bid_vol")]
	public double? BidVol { get; set; }
}