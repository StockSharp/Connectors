namespace StockSharp.Aster.Native.Common.Model;

class Trade
{
	[JsonProperty("a")]
	public long? AggregateId { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("T")]
	public long? TradeTime { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("m")]
	public bool? IsBuyerMaker { get; set; }
}
