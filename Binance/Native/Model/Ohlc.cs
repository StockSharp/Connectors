namespace StockSharp.Binance.Native.Model;

using System.Reflection;

class KLine
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime StartTime { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CloseTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

	[JsonProperty("f")]
	public long FirstTradeId { get; set; }

	[JsonProperty("L")]
	public long LastTradeId { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal AssetVolume { get; set; }

	[JsonProperty("n")]
	public int TradesCount { get; set; }

	[JsonProperty("x")]
	public bool IsFormed { get; set; }

	[JsonProperty("q")]
	public double QuoteVolume { get; set; }

	[JsonProperty("V")]
	public decimal? TakerBuyAssetVolume { get; set; }

	[JsonProperty("Q")]
	public decimal? TakerBuyQuoteVolume { get; set; }

	[JsonProperty("B")]
	public string Ignore { get; set; }
}

class Ohlc : BaseEvent
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("k")]
	public KLine Candle { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
[JsonConverter(typeof(JArrayToObjectConverter))]
class HttpOhlc
{
	public long StartTime { get; set; }

	public decimal Open { get; set; }

	public decimal High { get; set; }

	public decimal Low { get; set; }

	public decimal Close { get; set; }

	public decimal AssetVolume { get; set; }

	public long CloseTime { get; set; }

	public double QuoteVolume { get; set; }

	public int TradesCount { get; set; }

	public decimal? TakerBuyAssetVolume { get; set; }

	public decimal? TakerBuyQuoteVolume { get; set; }

	public string Ignore { get; set; }
}
