namespace StockSharp.Huobi.Native.Spot.Model;

class Symbol
{
	[JsonProperty("base-currency")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quote-currency")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("price-precision")]
	public int PricePrecision { get; set; }

	[JsonProperty("amount-precision")]
	public int AmountPrecision { get; set; }

	[JsonProperty("symbol-partition")]
	public string SymbolPartition { get; set; }
}