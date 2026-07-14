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
	public double? QuantityIncrement { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }

	[JsonProperty("minimum_position_size")]
	public double? MinimumPositionSize { get; set; }

	[JsonProperty("maximum_position_size")]
	public double? MaximumPositionSize { get; set; }

	[JsonProperty("price_increment")]
	public double? PriceIncrement { get; set; }

	[JsonProperty("minimum_price")]
	public double? MinimumPrice { get; set; }

	[JsonProperty("maximum_price")]
	public double? MaximumPrice { get; set; }

	[JsonProperty("asset_class")]
	public string AssetClass { get; set; }

	[JsonProperty("minimum_commission")]
	public double? MinimumCommission { get; set; }

	[JsonProperty("aggressive_commission_rate")]
	public double? AggressiveCommissionRate { get; set; }

	[JsonProperty("passive_commission_rate")]
	public double? PassiveCommissionRate { get; set; }

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
