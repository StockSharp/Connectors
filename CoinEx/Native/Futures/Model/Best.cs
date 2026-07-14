namespace StockSharp.CoinEx.Native.Futures.Model;

class Best
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("updated_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdatedAt { get; set; }

	[JsonProperty("best_bid_price")]
	public double? BidPrice { get; set; }

	[JsonProperty("best_bid_size")]
	public double? BidSize { get; set; }

	[JsonProperty("best_ask_price")]
	public double? AskPrice { get; set; }

	[JsonProperty("best_ask_size")]
	public double? AskSize { get; set; }
}