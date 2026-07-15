namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
interface IStreamingResponse
{
	[JsonProperty("type")]
	string Type { get; set; }
}