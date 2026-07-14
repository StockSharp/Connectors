namespace StockSharp.Mexc.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Candle
{
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTime { get; set; }
	
	public double Open { get; set; }
	public double High { get; set; }
	public double Low { get; set; }
	public double Close { get; set; }
	public double Volume { get; set; }
	
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CloseTime { get; set; }
	
	public double QuoteVolume { get; set; }
	public int TradeCount { get; set; }
	public double TakerBuyBaseVolume { get; set; }
	public double TakerBuyQuoteVolume { get; set; }
}

class CandleStream
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("k")]
	public CandleData Kline { get; set; }
}

class CandleData
{
	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime OpenTime { get; set; }

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
	public double QuoteVolume { get; set; }

	[JsonProperty("V")]
	public double TakerBuyBaseVolume { get; set; }

	[JsonProperty("Q")]
	public double TakerBuyQuoteVolume { get; set; }
}