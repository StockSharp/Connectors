namespace StockSharp.Deribit.Native.Model;

class Position
{
	[JsonProperty("average_price")]
	public double? AveragePrice { get; set; }

	[JsonProperty("delta")]
	public double? Delta { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("estimated_liquidation_price")]
	public double? EstimatedLiquidationPrice { get; set; }

	[JsonProperty("floating_profit_loss")]
	public double? FloatingPnL { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }

	[JsonProperty("initial_margin")]
	public double? InitialMargin { get; set; }

	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("kind")]
	public string Kind { get; set; }

	[JsonProperty("maintenance_margin")]
	public double? MaintenanceMargin { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("open_orders_margin")]
	public double? OpenOrdersMargin { get; set; }

	[JsonProperty("realized_profit_loss")]
	public double? RealizedPnL { get; set; }

	[JsonProperty("settlement_price")]
	public double? SettlementPrice { get; set; }

	[JsonProperty("size_currency")]
	public double? SizeCurrency { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("total_profit_loss")]
	public double? TotalPnL { get; set; }
}