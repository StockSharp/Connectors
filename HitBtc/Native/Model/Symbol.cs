namespace StockSharp.HitBtc.Native.Model;

class Symbol
{
	[JsonIgnore]
	public string Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("base_currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote_currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("quantity_increment")]
	public decimal QuantityIncrement { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("take_rate")]
	public decimal TakeLiquidityRate { get; set; }

	[JsonProperty("make_rate")]
	public decimal ProvideLiquidityRate { get; set; }

	[JsonProperty("fee_currency")]
	public string FeeCurrency { get; set; }
}
