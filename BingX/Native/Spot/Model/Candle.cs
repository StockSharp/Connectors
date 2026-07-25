namespace StockSharp.BingX.Native.Spot.Model;

class Candle
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime OpenTime { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
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
	public double Open { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("n")]
	public int TradeCount { get; set; }

	[JsonProperty("x")]
	public bool IsClosed { get; set; }

	[JsonProperty("q")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("V")]
	public double? TakerBuyBaseVolume { get; set; }

	[JsonProperty("Q")]
	public double? TakerBuyQuoteVolume { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class RestCandle
{
	// the endpoint answers with positional arrays of exactly eight elements
	// (open time, OHLC, volume, close time, quote volume), and the converter maps element to
	// field by position without honoring a per field converter, so the moments are read as
	// unix milliseconds and derived below
	public long OpenTimestamp { get; set; }
	public double Open { get; set; }
	public double High { get; set; }
	public double Low { get; set; }
	public double Close { get; set; }
	public double Volume { get; set; }
	public long CloseTimestamp { get; set; }
	public double QuoteAssetVolume { get; set; }

	public DateTime OpenTime => OpenTimestamp.FromUnix(false);
	public DateTime CloseTime => CloseTimestamp.FromUnix(false);
}