namespace StockSharp.Bitmart.Native.Futures.Model;

class Asset
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	// Position margin
	[JsonProperty("position_deposit")]
	public double? PositionDeposit { get; set; }

	[JsonProperty("frozen_balance")]
	public double? Frozen { get; set; }

	[JsonProperty("available_balance")]
	public double? Available { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("unrealized")]
	public double? Unrealized { get; set; }
}