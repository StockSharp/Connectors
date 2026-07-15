namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class CalendarsResponse
{
	[JsonProperty("calendars")]
	public IEnumerable<Calendar> Calendars { get; set; }
}