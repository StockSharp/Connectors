namespace StockSharp.BingX.Native.Futures.Model;

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

class RestCandle
{
	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("open")]
	public double Open { get; set; }

	[JsonProperty("high")]
	public double High { get; set; }

	[JsonProperty("low")]
	public double Low { get; set; }

	[JsonProperty("close")]
	public double Close { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("turnover")]
	public double? Turnover { get; set; }
}