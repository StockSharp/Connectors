namespace StockSharp.EodHistoricalData.Native.Model;

sealed class EodhdSymbol
{
	[JsonProperty("Code")]
	public string Code { get; set; }

	[JsonProperty("Name")]
	public string Name { get; set; }

	[JsonProperty("Country")]
	public string Country { get; set; }

	[JsonProperty("Exchange")]
	public string Exchange { get; set; }

	[JsonProperty("Currency")]
	public string Currency { get; set; }

	[JsonProperty("Type")]
	public string Type { get; set; }

	[JsonProperty("Isin")]
	public string Isin { get; set; }
}

sealed class EodhdSearchItem
{
	[JsonProperty("Code")]
	public string Code { get; set; }

	[JsonProperty("Exchange")]
	public string Exchange { get; set; }

	[JsonProperty("Name")]
	public string Name { get; set; }

	[JsonProperty("Type")]
	public string Type { get; set; }

	[JsonProperty("Country")]
	public string Country { get; set; }

	[JsonProperty("Currency")]
	public string Currency { get; set; }

	[JsonProperty("ISIN")]
	public string Isin { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("previousCloseDate")]
	public string PreviousCloseDate { get; set; }
}
