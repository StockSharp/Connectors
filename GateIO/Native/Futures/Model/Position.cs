namespace StockSharp.GateIO.Native.Futures.Model;

class Position
{
	[JsonProperty("user")]
	public long User { get; set; }

	[JsonProperty("contract")]
	public string Contract { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("risk_limit")]
	public double? RiskLimit { get; set; }

	[JsonProperty("leverage_max")]
	public double? LeverageMax { get; set; }

	[JsonProperty("maintenance_rate")]
	public double? MaintenanceRate { get; set; }

	[JsonProperty("value")]
	public double? Value { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }

	[JsonProperty("entry_price")]
	public double? EntryPrice { get; set; }

	[JsonProperty("liq_price")]
	public double? LiqPrice { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("realised_pnl")]
	public double? RealisedPnl { get; set; }

	[JsonProperty("pnl_pnl")]
	public double? PnlPnl { get; set; }

	[JsonProperty("pnl_fund")]
	public double? PnlFund { get; set; }

	[JsonProperty("pnl_fee")]
	public double? PnlFee { get; set; }

	[JsonProperty("history_pnl")]
	public double? HistoryPnl { get; set; }

	[JsonProperty("last_close_pnl")]
	public double? LastClosePnl { get; set; }

	[JsonProperty("realised_point")]
	public double? RealisedPoint { get; set; }

	[JsonProperty("history_point")]
	public double? HistoryPoint { get; set; }

	[JsonProperty("adl_ranking")]
	public int AdlRanking { get; set; }

	[JsonProperty("pending_orders")]
	public int PendingOrders { get; set; }

	[JsonProperty("close_order")]
	public PositionCloseOrder CloseOrder { get; set; }

	[JsonProperty("mode")]
	public string Mode { get; set; }

	[JsonProperty("update_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime? UpdateTime { get; set; }

	[JsonProperty("cross_leverage_limit")]
	public double? CrossLeverageLimit { get; set; }
}

class PositionCloseOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("is_liq")]
	public bool IsLiq { get; set; }
}