namespace StockSharp.HitBtc.Native.Model;

class Symbol
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("baseCurrency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("quantityIncrement")]
	public decimal QuantityIncrement { get; set; }

	[JsonProperty("tickSize")]
	public decimal TickSize { get; set; }

	[JsonProperty("takeLiquidityRate")]
	public decimal TakeLiquidityRate { get; set; }

	[JsonProperty("provideLiquidityRate")]
	public decimal ProvideLiquidityRate { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }
}