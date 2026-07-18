namespace StockSharp.FinancialModelingPrep.Native.Model;

sealed class FmpSymbolItem
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("companyName")]
	public string CompanyName { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("stockExchange")]
	public string StockExchange { get; set; }

	[JsonProperty("exchangeFullName")]
	public string ExchangeFullName { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeShortName")]
	public string ExchangeShortName { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("fromCurrency")]
	public string FromCurrency { get; set; }

	[JsonProperty("toCurrency")]
	public string ToCurrency { get; set; }

	[JsonProperty("fromName")]
	public string FromName { get; set; }

	[JsonProperty("toName")]
	public string ToName { get; set; }

	[JsonProperty("isEtf")]
	public bool? IsEtf { get; set; }

	[JsonProperty("isFund")]
	public bool? IsFund { get; set; }
}
