namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Ticker
{
	[JsonProperty("instType")]
	public string InstType { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("last")]
	public decimal? Last { get; set; }

	[JsonProperty("lastSz")]
	public decimal? LastSize { get; set; }

	[JsonProperty("askPx")]
	public decimal? BestAskPrice { get; set; }

	[JsonProperty("askSz")]
	public decimal? BestAskSize { get; set; }

	[JsonProperty("bidPx")]
	public decimal? BestBidPrice { get; set; }

	[JsonProperty("bidSz")]
	public decimal? BestBidSize { get; set; }

	[JsonProperty("open24h")]
	public decimal? Open24h { get; set; }

	[JsonProperty("high24h")]
	public decimal? High24h { get; set; }

	[JsonProperty("low24h")]
	public decimal? Low24h { get; set; }

	[JsonProperty("volCcy24h")]
	public decimal? VolCcy24h { get; set; }

	[JsonProperty("vol24h")]
	public decimal? Vol24h { get; set; }

	[JsonProperty("sodUtc0")]
	public decimal? SodUtc0 { get; set; }

	[JsonProperty("sodUtc8")]
	public decimal? SodUtc8 { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}