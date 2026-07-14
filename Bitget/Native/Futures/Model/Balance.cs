namespace StockSharp.Bitget.Native.Futures.Model;

class Balance
{
	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("locked")]
	public double? Locked { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("crossMaxAvailable")]
	public double? CrossMaxAvailable { get; set; }

	[JsonProperty("fixedMaxAvailable")]
	public double? FixedMaxAvailable { get; set; }

	[JsonProperty("maxTransferOut")]
	public double? MaxTransferOut { get; set; }

	[JsonProperty("equity")]
	public double? Equity { get; set; }

	[JsonProperty("usdtEquity")]
	public double? UsdtEquity { get; set; }

	[JsonProperty("btcEquity")]
	public double? BtcEquity { get; set; }

	[JsonProperty("crossRiskRate")]
	public double? CrossRiskRate { get; set; }

	[JsonProperty("unrealizedPL")]
	public double? UnrealizedPL { get; set; }

	[JsonProperty("bonus")]
	public double? Bonus { get; set; }
}