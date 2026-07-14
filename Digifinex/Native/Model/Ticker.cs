namespace StockSharp.Digifinex.Native.Model;

class Ticker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open_24h")]
	public double? Open24h { get; set; }

	[JsonProperty("low_24h")]
	public double? Low24h { get; set; }

	[JsonProperty("base_volume_24h")]
	public double? BaseVolume24h { get; set; }

	[JsonProperty("quote_volume_24h")]
	public double? QuoteVolume24h { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("last_qty")]
	public double? LastQty { get; set; }

	[JsonProperty("best_bid")]
	public double? BestBid { get; set; }

	[JsonProperty("best_bid_size")]
	public double? BestBidSize { get; set; }

	[JsonProperty("best_ask")]
	public double? BestAsk { get; set; }

	[JsonProperty("best_ask_size")]
	public double? BestAskSize { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}