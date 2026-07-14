namespace StockSharp.GateIO.Native.Spot.Model;

class Ticker
{
	[JsonProperty("currency_pair")]
	public string CurrencyPair { get; set; }

	[JsonProperty("last")]
	public double? Last { get; set; }

	[JsonProperty("lowest_ask")]
	public double? LowestAsk { get; set; }

	[JsonProperty("highest_bid")]
	public double? HighestBid { get; set; }

	[JsonProperty("change_percentage")]
	public double? ChangePercentage { get; set; }

	[JsonProperty("base_volume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("quote_volume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("high_24h")]
	public double? High24h { get; set; }

	[JsonProperty("low_24h")]
	public double? Low24h { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}