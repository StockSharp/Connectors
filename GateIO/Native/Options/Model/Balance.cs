namespace StockSharp.GateIO.Native.Options.Model;

class Balance
{
	[JsonProperty("user")]
	public string User { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("short_enabled")]
	public bool ShortEnabled { get; set; }

	[JsonProperty("total")]
	public double? Total { get; set; }

	[JsonProperty("unrealised_pnl")]
	public double? UnrealisedPnl { get; set; }

	[JsonProperty("init_margin")]
	public double? InitMargin { get; set; }

	[JsonProperty("maint_margin")]
	public double? MaintMargin { get; set; }

	[JsonProperty("order_margin")]
	public double? OrderMargin { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("balance")]
	public double? Value { get; set; }

	[JsonProperty("point")]
	public double? Point { get; set; }
}