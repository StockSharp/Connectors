namespace StockSharp.Bitget.Native.Spot.Model;

class Symbol
{
	[JsonProperty("symbol")]
	public string Id { get; set; }

	[JsonProperty("baseCoin")]
	public string BaseCoin { get; set; }

	[JsonProperty("quoteCoin")]
	public string QuoteCoin { get; set; }

	[JsonProperty("minTradeAmount")]
	public double? MinTradeAmount { get; set; }

	[JsonProperty("maxTradeAmount")]
	public double? MaxTradeAmount { get; set; }

	[JsonProperty("takerFeeRate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("makerFeeRate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("priceScale")]
	public int? PriceScale { get; set; }

	[JsonProperty("quantityScale")]
	public int? QuantityScale { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }
}
