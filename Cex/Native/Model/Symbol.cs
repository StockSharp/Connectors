namespace StockSharp.Cex.Native.Model;

class Symbol
{
	[JsonProperty("symbol1")]
	public string Symbol1 { get; set; }

	[JsonProperty("symbol2")]
	public string Symbol2 { get; set; }

	[JsonProperty("minLotSize")]
	public decimal? MinLotSize { get; set; }

	[JsonProperty("minLotSizeS2")]
	public decimal? MinLotSizeS2 { get; set; }

	[JsonProperty("maxLotSize")]
	public decimal? MaxLotSize { get; set; }

	[JsonProperty("minPrice")]
	public decimal? MinPrice { get; set; }

	[JsonProperty("maxPrice")]
	public decimal? MaxPrice { get; set; }
}