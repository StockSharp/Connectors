namespace StockSharp.Saxo.Native.Model;

sealed class SaxoInstrumentSummary
{
	[JsonProperty("Identifier")]
	public long Identifier { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("Description")]
	public string Description { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("SummaryType")]
	public string SummaryType { get; set; }

	[JsonProperty("DisplayHint")]
	public string DisplayHint { get; set; }

	[JsonProperty("ExchangeId")]
	public string ExchangeId { get; set; }
}

sealed class SaxoExchange
{
	[JsonProperty("ExchangeId")]
	public string ExchangeId { get; set; }

	[JsonProperty("Name")]
	public string Name { get; set; }

	[JsonProperty("CountryCode")]
	public string CountryCode { get; set; }
}

sealed class SaxoPriceFormat
{
	[JsonProperty("Decimals")]
	public int? Decimals { get; set; }

	[JsonProperty("OrderDecimals")]
	public int? OrderDecimals { get; set; }
}

sealed class SaxoSupportedOrderTypeSetting
{
	[JsonProperty("OrderType")]
	public string OrderType { get; set; }

	[JsonProperty("DurationTypes")]
	public string[] DurationTypes { get; set; }
}

sealed class SaxoInstrumentDetails
{
	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("Description")]
	public string Description { get; set; }

	[JsonProperty("CurrencyCode")]
	public string CurrencyCode { get; set; }

	[JsonProperty("Exchange")]
	public SaxoExchange Exchange { get; set; }

	[JsonProperty("ExpiryDate")]
	public DateTime? ExpiryDate { get; set; }

	[JsonProperty("PutCall")]
	public string PutCall { get; set; }

	[JsonProperty("StrikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("ContractSize")]
	public decimal? ContractSize { get; set; }

	[JsonProperty("TickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("TickSizeLimitOrder")]
	public decimal? TickSizeLimitOrder { get; set; }

	[JsonProperty("Format")]
	public SaxoPriceFormat Format { get; set; }

	[JsonProperty("IsTradable")]
	public bool IsTradable { get; set; }

	[JsonProperty("SupportedOrderTypeSettings")]
	public SaxoSupportedOrderTypeSetting[] SupportedOrderTypeSettings { get; set; }
}

sealed class SaxoFutureSpace
{
	[JsonProperty("Elements")]
	public SaxoFutureContract[] Elements { get; set; }
}

sealed class SaxoFutureContract
{
	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }

	[JsonProperty("Description")]
	public string Description { get; set; }

	[JsonProperty("ExpiryDate")]
	public DateTime? ExpiryDate { get; set; }
}

sealed class SaxoOptionSpace
{
	[JsonProperty("AssetType")]
	public string AssetType { get; set; }

	[JsonProperty("Description")]
	public string Description { get; set; }

	[JsonProperty("Exchange")]
	public SaxoExchange Exchange { get; set; }

	[JsonProperty("OptionSpace")]
	public SaxoOptionSeries[] OptionSpace { get; set; }
}

sealed class SaxoOptionSeries
{
	[JsonProperty("DisplayExpiry")]
	public string DisplayExpiry { get; set; }

	[JsonProperty("ExpiryDate")]
	public DateTime? ExpiryDate { get; set; }

	[JsonProperty("SpecificOptions")]
	public SaxoSpecificOption[] SpecificOptions { get; set; }
}

sealed class SaxoSpecificOption
{
	[JsonProperty("Uic")]
	public long Uic { get; set; }

	[JsonProperty("StrikePrice")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("PutCall")]
	public string PutCall { get; set; }

	[JsonProperty("Symbol")]
	public string Symbol { get; set; }
}

sealed class SaxoInstrument
{
	public long Uic { get; set; }
	public string AssetType { get; set; }
	public string Symbol { get; set; }
	public string Description { get; set; }
	public string Currency { get; set; }
	public string Exchange { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? Strike { get; set; }
	public string PutCall { get; set; }
	public decimal? Multiplier { get; set; }
	public decimal? PriceStep { get; set; }
	public SaxoSupportedOrderTypeSetting[] SupportedOrderTypeSettings { get; set; }
}
