namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class CandlesResponse
{
	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("granularity")]
	public string TimeFrame { get; set; }

	[JsonProperty("candles")]
	public IEnumerable<Candle> Candles { get; set; }
}