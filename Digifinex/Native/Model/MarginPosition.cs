namespace StockSharp.Digifinex.Native.Model;

class MarginPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("leverage_ratio")]
	public double? LeverageRatio { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("entry_price")]
	public double? EntryPrice { get; set; }

	[JsonProperty("unrealized_pnl")]
	public double? UnrealizedPnl { get; set; }

	[JsonProperty("liquidation_price")]
	public double? LiquidationPrice { get; set; }
}