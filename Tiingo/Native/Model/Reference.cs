namespace StockSharp.Tiingo.Native.Model;

sealed class TiingoSearchItem
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("assetType")]
	public string AssetType { get; set; }

	[JsonProperty("isActive")]
	public bool? IsActive { get; set; }

	[JsonProperty("permaTicker")]
	public string PermaTicker { get; set; }

	[JsonProperty("openFIGI")]
	public string OpenFigi { get; set; }
}

sealed class TiingoSupportedTicker
{
	public string Ticker { get; set; }
	public string Exchange { get; set; }
	public string AssetType { get; set; }
	public string PriceCurrency { get; set; }
	public string StartDate { get; set; }
	public string EndDate { get; set; }
}

sealed class TiingoEodMeta
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("startDate")]
	public string StartDate { get; set; }

	[JsonProperty("endDate")]
	public string EndDate { get; set; }

	[JsonProperty("exchangeCode")]
	public string ExchangeCode { get; set; }
}

sealed class TiingoCryptoMeta
{
	[JsonProperty("ticker")]
	public string Ticker { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }
}
