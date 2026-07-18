namespace StockSharp.Finnhub.Native.Model;

sealed class FinnhubExchange
{
	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("mic")]
	public string Mic { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }
}

sealed class FinnhubStockSymbol
{
	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("mic")]
	public string Mic { get; set; }

	[JsonProperty("figi")]
	public string Figi { get; set; }

	[JsonProperty("shareClassFIGI")]
	public string ShareClassFigi { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("symbol2")]
	public string Symbol2 { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }
}

sealed class FinnhubAssetSymbol
{
	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class FinnhubSymbolLookupResponse
{
	[JsonProperty("count")]
	public long? Count { get; set; }

	[JsonProperty("result")]
	public FinnhubSymbolLookupItem[] Result { get; set; }
}

sealed class FinnhubSymbolLookupItem
{
	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("displaySymbol")]
	public string DisplaySymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}
