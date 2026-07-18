namespace StockSharp.EodHistoricalData.Native.Model;

sealed class EodhdErrorResponse
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class EodhdExchange
{
	[JsonProperty("Name")]
	public string Name { get; set; }

	[JsonProperty("Code")]
	public string Code { get; set; }

	[JsonProperty("OperatingMIC")]
	public string OperatingMic { get; set; }

	[JsonProperty("Country")]
	public string Country { get; set; }

	[JsonProperty("Currency")]
	public string Currency { get; set; }

	[JsonProperty("CountryISO2")]
	public string CountryIso2 { get; set; }

	[JsonProperty("CountryISO3")]
	public string CountryIso3 { get; set; }
}
