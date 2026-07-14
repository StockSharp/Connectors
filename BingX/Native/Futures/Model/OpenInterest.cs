namespace StockSharp.BingX.Native.Futures.Model;

class OpenInterest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("openInterest")]
	public double? OpenInterestValue { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Time { get; set; }
}
