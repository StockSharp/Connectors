namespace StockSharp.LBank.Native.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Code { get; set; }

	[JsonProperty("quantityAccuracy")]
	public int QuantityAccuracy { get; set; }

	[JsonProperty("minTranQua")]
	public double MinTranQua { get; set; }

	[JsonProperty("priceAccuracy")]
	public int PriceAccuracy { get; set; }
}