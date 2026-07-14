namespace StockSharp.Huobi.Native.Futures.Model;

class Balance
{
	[JsonProperty("symbol")]
	public string Asset { get; set; }

	[JsonProperty("margin_balance")]
	public double? MarginBalance { get; set; }

	[JsonProperty("margin_position")]
	public double? MarginPosition { get; set; }

	[JsonProperty("margin_frozen")]
	public double? MarginFrozen { get; set; }

	[JsonProperty("margin_available")]
	public double? MarginAvailable { get; set; }

	[JsonProperty("profit_real")]
	public double? ProfitReal { get; set; }

	[JsonProperty("profit_unreal")]
	public double? ProfitUnreal { get; set; }

	[JsonProperty("withdraw_available")]
	public double? WithdrawAvailable { get; set; }

	[JsonProperty("risk_rate")]
	public double? RiskRate { get; set; }

	[JsonProperty("liquidation_price")]
	public double? LiquidationPrice { get; set; }

	[JsonProperty("adjust_factor")]
	public double? AdjustFactor { get; set; }

	[JsonProperty("lever_rate")]
	public double? LeverRate { get; set; }

	[JsonProperty("margin_static")]
	public double? MarginStatic { get; set; }
}