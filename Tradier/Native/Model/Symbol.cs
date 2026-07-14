namespace StockSharp.Tradier.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Symbol
{
	[JsonProperty("symbol")]
	public string Code { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public TradierSecurityTypes Type { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}
