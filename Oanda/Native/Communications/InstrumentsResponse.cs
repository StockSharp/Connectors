namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class InstrumentsResponse
{
	[JsonProperty("instruments")]
	public IEnumerable<Instrument> Instruments { get; set; }
}