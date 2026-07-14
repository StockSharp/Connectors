namespace StockSharp.Bitbank.Native.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlc
{
	public double Open { get; set; }

	public double High { get; set; }

	public double Low { get; set; }

	public double Close { get; set; }

	public double Volume { get; set; }

	//[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public long Time { get; set; }
}

class OhlcResponseItem
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("ohlcv")]
	public Ohlc[] Candles { get; set; }
}

class OhlcResponse
{
	[JsonProperty("candlestick")]
	public OhlcResponseItem[] Items { get; set; }
}