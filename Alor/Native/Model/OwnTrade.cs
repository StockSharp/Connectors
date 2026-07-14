namespace StockSharp.Alor.Native.Model;

class OwnTrade : ISymbolObject
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("orderno")]
	public long Order { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("brokerSymbol")]
	public string BrokerSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("date")]
	public DateTime Date { get; set; }

	[JsonProperty("board")]
	public string Board { get; set; }

	[JsonProperty("qtyUnits")]
	public long QtyUnits { get; set; }

	[JsonProperty("qtyBatch")]
	public long QtyBatch { get; set; }

	[JsonProperty("qty")]
	public long Qty { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("existing")]
	public bool Existing { get; set; }
}