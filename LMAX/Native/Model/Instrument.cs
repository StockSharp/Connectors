namespace StockSharp.LMAX.Native.Model;

class Instrument
{
	[JsonProperty("instrument_id")]
	public string InstrumentId { get; set; }

	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("unit_of_measure")]
	public string UnitOfMeasure { get; set; }

	[JsonProperty("quantity_increment")]
	public string QuantityIncrement { get; set; }

	[JsonProperty("margin")]
	public string Margin { get; set; }

	[JsonProperty("minimum_position_size")]
	public string MinimumPositionSize { get; set; }

	[JsonProperty("maximum_position_size")]
	public string MaximumPositionSize { get; set; }

	[JsonProperty("price_increment")]
	public string PriceIncrement { get; set; }

	[JsonProperty("minimum_price")]
	public string MinimumPrice { get; set; }

	[JsonProperty("maximum_price")]
	public string MaximumPrice { get; set; }

	[JsonProperty("asset_class")]
	public string AssetClass { get; set; }

	[JsonProperty("minimum_commission")]
	public string MinimumCommission { get; set; }

	[JsonProperty("aggressive_commission_per_unit_of_measure")]
	public string AggressiveCommissionPerUnitOfMeasure { get; set; }

	[JsonProperty("passive_commission_per_unit_of_measure")]
	public string PassiveCommissionPerUnitOfMeasure { get; set; }

	[JsonProperty("aggressive_commission_rate")]
	public string AggressiveCommissionRate { get; set; }

	[JsonProperty("passive_commission_rate")]
	public string PassiveCommissionRate { get; set; }

	[JsonProperty("open_time")]
	public string OpenTime { get; set; }

	[JsonProperty("close_time")]
	public string CloseTime { get; set; }

	[JsonProperty("time_zone")]
	public string TimeZone { get; set; }

	[JsonProperty("trading_days")]
	public string[] TradingDays { get; set; }
}

class InstrumentDataResponse
{
	[JsonProperty("account_id")]
	public string AccountId { get; set; }

	[JsonProperty("timestamp")]
	public DateTime Timestamp { get; set; }

	[JsonProperty("instruments")]
	public Instrument[] Instruments { get; set; }
}
