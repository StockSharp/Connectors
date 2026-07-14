namespace StockSharp.Bitmart.Native.Futures.Model;

interface IOhlc
{
	DateTime Time { get; set; }
	double Open { get; set; }
	double High { get; set; }
	double Low { get; set; }
	double Close { get; set; }
	double Volume { get; set; }
}

class Ohlc : IOhlc
{
	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("o")]
	public double Open { get; set; }

	[JsonProperty("h")]
	public double High { get; set; }

	[JsonProperty("l")]
	public double Low { get; set; }

	[JsonProperty("c")]
	public double Close { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }
}

class RestOhlc : IOhlc
{
	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("open_price")]
	public double Open { get; set; }

	[JsonProperty("high_price")]
	public double High { get; set; }

	[JsonProperty("low_price")]
	public double Low { get; set; }

	[JsonProperty("close_price")]
	public double Close { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }
}