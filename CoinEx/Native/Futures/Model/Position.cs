namespace StockSharp.CoinEx.Native.Futures.Model;

class Position
{
	[JsonProperty("position_id")]
	public long PositionId { get; set; }

	[JsonProperty("market")]
	public string Symbol { get; set; }

	[JsonProperty("market_type")]
	public string MarketType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("margin_mode")]
	public string MarginMode { get; set; }

	[JsonProperty("open_interest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("close_avbl")]
	public double? CloseAvailable { get; set; }

	[JsonProperty("ath_position_amount")]
	public double? AthPositionAmount { get; set; }

	[JsonProperty("unrealized_pnl")]
	public double? UnrealizedPnl { get; set; }

	[JsonProperty("realized_pnl")]
	public double? RealizedPnl { get; set; }

	[JsonProperty("avg_entry_price")]
	public double? AvgEntryPrice { get; set; }

	[JsonProperty("cml_position_value")]
	public double? CmlPositionValue { get; set; }

	[JsonProperty("max_position_value")]
	public double? MaxPositionValue { get; set; }

	[JsonProperty("take_profit_price")]
	public double? TakeProfitPrice { get; set; }

	[JsonProperty("stop_loss_price")]
	public double? StopLossPrice { get; set; }

	[JsonProperty("take_profit_type")]
	public string TakeProfitType { get; set; }

	[JsonProperty("stop_loss_type")]
	public string StopLossType { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("margin_avbl")]
	public double? MarginAvailable { get; set; }

	[JsonProperty("ath_margin_size")]
	public double? AthMarginSize { get; set; }

	[JsonProperty("position_margin_rate")]
	public double? PositionMarginRate { get; set; }

	[JsonProperty("maintenance_margin_rate")]
	public double? MaintenanceMarginRate { get; set; }

	[JsonProperty("maintenance_margin_value")]
	public double? MaintenanceMarginValue { get; set; }

	[JsonProperty("liq_price")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("bkr_price")]
	public double? BankruptcyPrice { get; set; }

	[JsonProperty("adl_level")]
	public int? AdlLevel { get; set; }

	[JsonProperty("settle_price")]
	public double? SettlePrice { get; set; }

	[JsonProperty("settle_value")]
	public double? SettleValue { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdatedAt { get; set; }
}