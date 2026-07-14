namespace StockSharp.Bitmex.Native.Model;

class Commission
{
	[JsonProperty("maxFee")]
	public double? MaxFee { get; set; }

	[JsonProperty("makerFee")]
	public double? MakerFee { get; set; }

	[JsonProperty("takerFee")]
	public double? TakerFee { get; set; }

	[JsonProperty("settlementFee")]
	public double? SettlementFee { get; set; }
}