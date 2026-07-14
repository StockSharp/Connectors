namespace StockSharp.Alor.Native.Model;

class Order : ISymbolObject
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("portfolio")]
	public string Portfolio { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("brokerSymbol")]
	public string BrokerSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("transTime")]
	public DateTime TransTime { get; set; }

	[JsonProperty("updateTime")]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("endTime")]
	public DateTime? EndTime { get; set; }

	[JsonProperty("qtyUnits")]
	public long QtyUnits { get; set; }

	[JsonProperty("qtyBatch")]
	public long QtyBatch { get; set; }

	[JsonProperty("qty")]
	public long Qty { get; set; }

	[JsonProperty("filledQtyUnits")]
	public long FilledQtyUnits { get; set; }

	[JsonProperty("filledQtyBatch")]
	public long FilledQtyBatch { get; set; }

	[JsonProperty("filled")]
	public long Filled { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("stopPrice")]
	public double? StopPrice { get; set; }

	[JsonProperty("existing")]
	public bool Existing { get; set; }

	[JsonProperty("comment")]
	public string Comment { get; set; }
}