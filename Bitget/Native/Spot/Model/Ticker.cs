namespace StockSharp.Bitget.Native.Spot.Model;

class Ticker
{
	[JsonProperty("instId")]
	public string Symbol { get; set; }

	[JsonProperty("high24h")]
	public double? High24h { get; set; }

	[JsonProperty("low24h")]
	public double? Low24h { get; set; }

	[JsonProperty("lastPr")]
	public double? LastPrice { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("usdtVolume")]
	public double? UsdtVolume { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("bidPr")]
	public double? BidPrice { get; set; }

	[JsonProperty("askPr")]
	public double? AskPrice { get; set; }

	[JsonProperty("bidSz")]
	public double? BidSize { get; set; }

	[JsonProperty("askSz")]
	public double? AskSize { get; set; }

	[JsonProperty("openUtc")]
	public double? OpenUtc { get; set; }

	[JsonProperty("changeUtc24h")]
	public double? ChangeUtc24h { get; set; }

	[JsonProperty("change")]
	public double? Change { get; set; }
}
