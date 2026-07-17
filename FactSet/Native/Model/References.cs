namespace StockSharp.FactSet.Native.Model;

sealed class FactSetReferencesResponse
{
	[JsonProperty("data")]
	public FactSetReference[] Data { get; set; }
}

sealed class FactSetReference
{
	[JsonProperty("fsymId")]
	public string FsymId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("secType")]
	public string SecType { get; set; }

	[JsonProperty("secTypeCode")]
	public string SecTypeCode { get; set; }

	[JsonProperty("secTypeCodeDet")]
	public string SecTypeCodeDetail { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("primaryExchange")]
	public string PrimaryExchange { get; set; }

	[JsonProperty("exchangeCountry")]
	public string ExchangeCountry { get; set; }

	[JsonProperty("localIndex")]
	public string LocalIndex { get; set; }

	[JsonProperty("nextTradingHolidayDate")]
	public string NextTradingHolidayDate { get; set; }

	[JsonProperty("firstDate")]
	public string FirstDate { get; set; }

	[JsonProperty("lastDate")]
	public string LastDate { get; set; }

	[JsonProperty("requestId")]
	public string RequestId { get; set; }
}

sealed class FactSetReferenceQuery
{
	public string Id { get; init; }

	public string ToQueryString()
		=> $"ids={Uri.EscapeDataString(Id ?? string.Empty)}";
}
