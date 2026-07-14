namespace StockSharp.AlphaVantage.Native.Model;

using System.Reflection;

using Newtonsoft.Json;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class Symbol
{
	[JsonProperty("1. symbol")]
	public string Code { get; set; }

	[JsonProperty("2. name")]
	public string Name { get; set; }

	[JsonProperty("3. type")]
	public string Type { get; set; }

	[JsonProperty("4. region")]
	public string Region { get; set; }

	[JsonProperty("5. marketOpen")]
	public string MarketOpen { get; set; }

	[JsonProperty("6. marketClose")]
	public string MarketClose { get; set; }

	[JsonProperty("7. timezone")]
	public string Timezone { get; set; }

	[JsonProperty("8. currency")]
	public string Currency { get; set; }

	[JsonProperty("9. matchScore")]
	public string MatchScore { get; set; }
}