namespace StockSharp.Alpaca.Native.Model;

class Position
{
	[JsonProperty("asset_id")]
	public string AssetId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("asset_class")]
	public string AssetClass { get; set; }

	[JsonProperty("avg_entry_price")]
	public double? AvgEntryPrice { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("qty_available")]
	public double? QtyAvailable { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("market_value")]
	public double? MarketValue { get; set; }

	[JsonProperty("cost_basis")]
	public double? CostBasis { get; set; }

	[JsonProperty("unrealized_pl")]
	public double? UnrealizedPl { get; set; }

	[JsonProperty("unrealized_plpc")]
	public double? UnrealizedPlpc { get; set; }

	[JsonProperty("unrealized_intraday_pl")]
	public double? UnrealizedIntradayPl { get; set; }

	[JsonProperty("unrealized_intraday_plpc")]
	public double? UnrealizedIntradayPlpc { get; set; }

	[JsonProperty("current_price")]
	public double? CurrentPrice { get; set; }

	[JsonProperty("lastday_price")]
	public double? LastdayPrice { get; set; }

	[JsonProperty("change_today")]
	public double? ChangeToday { get; set; }
}