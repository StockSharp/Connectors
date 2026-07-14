namespace StockSharp.GateIO.Native.Delivery.Model;

class Ticker
{
	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("change_percentage")]
	public double? ChangePercentage { get; set; }

	[JsonProperty("total_size")]
	public double? TotalSize { get; set; }

	[JsonProperty("volume_24h")]
	public double? Volume24h { get; set; }

	[JsonProperty("volume_24h_base")]
	public double? Volume24hBase { get; set; }

	[JsonProperty("volume_24h_quote")]
	public double? Volume24hQuote { get; set; }

	[JsonProperty("volume_24h_settle")]
	public double? Volume24hSettle { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("funding_rate")]
	public double? FundingRate { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("low_24h")]
	public double? Low24h { get; set; }

	[JsonProperty("high_24h")]
	public double? High24h { get; set; }

	[JsonProperty("open_interest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("highest_bid")]
	public double? HighestBid { get; set; }

	[JsonProperty("lowest_ask")]
	public double? LowestAsk { get; set; }
}