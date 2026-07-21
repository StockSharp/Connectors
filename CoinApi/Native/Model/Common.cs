namespace StockSharp.CoinApi.Native.Model;

sealed class CoinApiError
{
	[JsonProperty("error")]
	public string Error { get; set; }
}

sealed class CoinApiPeriod
{
	[JsonProperty("period_id")]
	public CoinApiPeriodIds PeriodId { get; set; }

	[JsonProperty("length_seconds")]
	public long LengthSeconds { get; set; }

	[JsonProperty("length_months")]
	public int LengthMonths { get; set; }

	[JsonProperty("unit_count")]
	public int UnitCount { get; set; }

	[JsonProperty("unit_name")]
	public string UnitName { get; set; }

	[JsonProperty("display_name")]
	public string DisplayName { get; set; }
}

sealed class CoinApiSymbol
{
	[JsonProperty("symbol_id")]
	public string SymbolId { get; set; }

	[JsonProperty("exchange_id")]
	public string ExchangeId { get; set; }

	[JsonProperty("symbol_type")]
	public CoinApiSymbolTypes SymbolType { get; set; }

	[JsonProperty("asset_id_base")]
	public string BaseAsset { get; set; }

	[JsonProperty("asset_id_quote")]
	public string QuoteAsset { get; set; }

	[JsonProperty("asset_id_unit")]
	public string UnitAsset { get; set; }

	[JsonProperty("symbol_id_exchange")]
	public string ExchangeSymbolId { get; set; }

	[JsonProperty("asset_id_base_exchange")]
	public string ExchangeBaseAsset { get; set; }

	[JsonProperty("asset_id_quote_exchange")]
	public string ExchangeQuoteAsset { get; set; }

	[JsonProperty("price_precision")]
	public decimal? PricePrecision { get; set; }

	[JsonProperty("size_precision")]
	public decimal? SizePrecision { get; set; }

	[JsonProperty("volume_1day_usd")]
	public decimal? VolumeOneDayUsd { get; set; }

	[JsonProperty("future_delivery_time")]
	public string FutureDeliveryTime { get; set; }

	[JsonProperty("future_contract_unit")]
	public decimal? FutureContractUnit { get; set; }

	[JsonProperty("future_contract_unit_asset")]
	public string FutureContractUnitAsset { get; set; }

	[JsonProperty("option_type_is_call")]
	public bool? IsOptionCall { get; set; }

	[JsonProperty("option_strike_price")]
	public decimal? OptionStrikePrice { get; set; }

	[JsonProperty("option_contract_unit")]
	public decimal? OptionContractUnit { get; set; }

	[JsonProperty("option_expiration_time")]
	public string OptionExpirationTime { get; set; }

	[JsonProperty("contract_delivery_time")]
	public string ContractDeliveryTime { get; set; }

	[JsonProperty("contract_unit")]
	public decimal? ContractUnit { get; set; }

	[JsonProperty("contract_unit_asset")]
	public string ContractUnitAsset { get; set; }

	[JsonProperty("contract_id")]
	public string ContractId { get; set; }

	[JsonProperty("index_id")]
	public string IndexId { get; set; }

	[JsonProperty("index_display_name")]
	public string IndexDisplayName { get; set; }

	[JsonProperty("index_display_description")]
	public string IndexDisplayDescription { get; set; }
}
