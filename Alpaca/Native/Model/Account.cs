namespace StockSharp.Alpaca.Native.Model;

class Account
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("admin_configurations")]
	public object AdminConfigurations { get; set; }

	[JsonProperty("user_configurations")]
	public object UserConfigurations { get; set; }

	[JsonProperty("account_number")]
	public string AccountNumber { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("crypto_status")]
	public string CryptoStatus { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("buying_power")]
	public double? BuyingPower { get; set; }

	[JsonProperty("regt_buying_power")]
	public double? RegtBuyingPower { get; set; }

	[JsonProperty("daytrading_buying_power")]
	public double? DaytradingBuyingPower { get; set; }

	[JsonProperty("effective_buying_power")]
	public double? EffectiveBuyingPower { get; set; }

	[JsonProperty("non_marginable_buying_power")]
	public double? NonMarginableBuyingPower { get; set; }

	[JsonProperty("bod_dtbp")]
	public double? BodDtbp { get; set; }

	[JsonProperty("cash")]
	public double? Cash { get; set; }

	[JsonProperty("accrued_fees")]
	public double? AccruedFees { get; set; }

	[JsonProperty("pending_transfer_in")]
	public double? PendingTransferIn { get; set; }

	[JsonProperty("portfolio_value")]
	public double? PortfolioValue { get; set; }

	[JsonProperty("pattern_day_trader")]
	public bool PatternDayTrader { get; set; }

	[JsonProperty("trading_blocked")]
	public bool TradingBlocked { get; set; }

	[JsonProperty("transfers_blocked")]
	public bool TransfersBlocked { get; set; }

	[JsonProperty("account_blocked")]
	public bool AccountBlocked { get; set; }

	[JsonProperty("created_at")]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("trade_suspended_by_user")]
	public bool TradeSuspendedByUser { get; set; }

	[JsonProperty("multiplier")]
	public int? Multiplier { get; set; }

	[JsonProperty("shorting_enabled")]
	public bool ShortingEnabled { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("last_equity")]
	public double? LastEquity { get; set; }

	[JsonProperty("long_market_value")]
	public double? LongMarketValue { get; set; }

	[JsonProperty("short_market_value")]
	public double? ShortMarketValue { get; set; }

	[JsonProperty("position_market_value")]
	public double? PositionMarketValue { get; set; }

	[JsonProperty("initial_margin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("maintenance_margin")]
	public double? MaintenanceMargin { get; set; }

	[JsonProperty("last_maintenance_margin")]
	public double? LastMaintenanceMargin { get; set; }

	[JsonProperty("sma")]
	public double? Sma { get; set; }

	[JsonProperty("daytrade_count")]
	public int DaytradeCount { get; set; }

	[JsonProperty("balance_asof")]
	public DateTime? BalanceAsof { get; set; }

	[JsonProperty("crypto_tier")]
	public int CryptoTier { get; set; }
}
