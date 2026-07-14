namespace StockSharp.CoinEx.Native.Futures.Model;

class Balance
{
	[JsonProperty("ccy")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("frozen")]
	public double? Frozen { get; set; }

	[JsonProperty("margin")]
	public double? Margin { get; set; }

	[JsonProperty("unrealized_pnl")]
	public double? UnrealizedPnl { get; set; }

	[JsonProperty("transferrable")]
	public double? Transferrable { get; set; }
}