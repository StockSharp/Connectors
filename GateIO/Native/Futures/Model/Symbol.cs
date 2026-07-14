namespace StockSharp.GateIO.Native.Futures.Model;

class Symbol
{
	[JsonProperty("funding_rate_indicative")]
	public double? FundingRateIndicative { get; set; }

	[JsonProperty("mark_price_round")]
	public double? MarkPriceRound { get; set; }

	[JsonProperty("funding_offset")]
	public double? FundingOffset { get; set; }

	[JsonProperty("in_delisting")]
	public bool? InDelisting { get; set; }

	[JsonProperty("risk_limit_base")]
	public double? RiskLimitBase { get; set; }

	[JsonProperty("interest_rate")]
	public double? InterestRate { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("order_price_round")]
	public double? OrderPriceRound { get; set; }

	[JsonProperty("order_size_min")]
	public double? OrderSizeMin { get; set; }

	[JsonProperty("ref_rebate_rate")]
	public double? RefRebateRate { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("ref_discount_rate")]
	public double? RefDiscountRate { get; set; }

	[JsonProperty("order_price_deviate")]
	public double? OrderPriceDeviate { get; set; }

	[JsonProperty("maintenance_rate")]
	public double? MaintenanceRate { get; set; }

	[JsonProperty("mark_type")]
	public string MarkType { get; set; }

	[JsonProperty("funding_interval")]
	public int? FundingInterval { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("risk_limit_step")]
	public double? RiskLimitStep { get; set; }

	[JsonProperty("enable_bonus")]
	public bool? EnableBonus { get; set; }

	[JsonProperty("enable_credit")]
	public bool? EnableCredit { get; set; }

	[JsonProperty("leverage_min")]
	public double? LeverageMin { get; set; }

	[JsonProperty("funding_rate")]
	public double? FundingRate { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("order_size_max")]
	public double? OrderSizeMax { get; set; }

	[JsonProperty("funding_next_apply")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime FundingNextApply { get; set; }

	[JsonProperty("short_users")]
	public int? ShortUsers { get; set; }

	[JsonProperty("config_change_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime ConfigChangeTime { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("trade_size")]
	public double TradeSize { get; set; }

	[JsonProperty("position_size")]
	public double PositionSize { get; set; }

	[JsonProperty("long_users")]
	public int LongUsers { get; set; }

	[JsonProperty("quanto_multiplier")]
	public double? QuantoMultiplier { get; set; }

	[JsonProperty("funding_impact_value")]
	public double? FundingImpactValue { get; set; }

	[JsonProperty("leverage_max")]
	public double? LeverageMax { get; set; }

	[JsonProperty("cross_leverage_default")]
	public double? CrossLeverageDefault { get; set; }

	[JsonProperty("risk_limit_max")]
	public double? RiskLimitMax { get; set; }

	[JsonProperty("maker_fee_rate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("taker_fee_rate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("orders_limit")]
	public int OrdersLimit { get; set; }

	[JsonProperty("trade_id")]
	public long TradeId { get; set; }

	[JsonProperty("orderbook_id")]
	public long OrderbookId { get; set; }

	[JsonProperty("funding_cap_ratio")]
	public double? FundingCapRatio { get; set; }

	[JsonProperty("voucher_leverage")]
	public double? VoucherLeverage { get; set; }
}