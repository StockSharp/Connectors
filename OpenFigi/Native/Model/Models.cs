namespace StockSharp.OpenFigi.Native;

sealed class OpenFigiInstrument
{
	[JsonProperty("figi")]
	public string Figi { get; set; }

	[JsonProperty("securityType")]
	public string SecurityType { get; set; }

	[JsonProperty("marketSector")]
	public string MarketSector { get; set; }

	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exchCode")]
	public string ExchangeCode { get; set; }

	[JsonProperty("shareClassFIGI")]
	public string ShareClassFigi { get; set; }

	[JsonProperty("compositeFIGI")]
	public string CompositeFigi { get; set; }

	[JsonProperty("securityType2")]
	public string SecurityType2 { get; set; }

	[JsonProperty("securityDescription")]
	public string SecurityDescription { get; set; }
}

readonly record struct OpenFigiLookupRequest(
	JObject Mapping,
	JObject Criteria,
	bool UseSearch,
	string IdentifierType,
	string IdentifierValue);
