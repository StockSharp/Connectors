namespace StockSharp.Oanda.Native.Communications;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class StreamingPricingResponse : Pricing, IStreamingResponse
{
	[JsonProperty("type")]
	public string Type { get; set; }
}