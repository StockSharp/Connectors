namespace StockSharp.TwelveData.Native.Model;

sealed class TwelveDataReferenceResponse : TwelveDataResponse
{
	[JsonProperty("data")]
	public TwelveDataReferenceItem[] Data { get; set; }

	[JsonProperty("count")]
	public long? Count { get; set; }
}

sealed class TwelveDataReferenceItem
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("instrument_name")]
	public string InstrumentName { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("available_exchanges")]
	public string[] AvailableExchanges { get; set; }

	[JsonProperty("mic_code")]
	public string MicCode { get; set; }

	[JsonProperty("exchange_timezone")]
	public string ExchangeTimezone { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("currency_group")]
	public string CurrencyGroup { get; set; }

	[JsonProperty("currency_base")]
	public string CurrencyBase { get; set; }

	[JsonProperty("currency_quote")]
	public string CurrencyQuote { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("instrument_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("figi_code")]
	public string FigiCode { get; set; }

	[JsonProperty("cfi_code")]
	public string CfiCode { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("cusip")]
	public string Cusip { get; set; }
}
