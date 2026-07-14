namespace StockSharp.Kucoin.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class Ohlc
{
	//[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public long Time { get; set; }

	public double Open { get; set; }

	public double Close { get; set; }

	public double High { get; set; }

	public double Low { get; set; }

	public double Volume { get; set; }

	public double Turnover  { get; set; }
}