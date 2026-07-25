namespace StockSharp.Aster.Native.Common.Model;

class Trade
{
	[JsonProperty("a")]
	public long? AggregateId { get; set; }

	[JsonProperty("id")]
	public long? Id { get; set; }

	[JsonProperty("p")]
	public string AggPrice { get; set; }

	[JsonProperty("price")]
	public string RawPrice { get; set; }

	[JsonProperty("q")]
	public string AggQuantity { get; set; }

	[JsonProperty("qty")]
	public string RawQuantity { get; set; }

	[JsonProperty("T")]
	public long? TradeTime { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("m")]
	public bool? AggIsBuyerMaker { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool? RawIsBuyerMaker { get; set; }

	// aggTrades reports the fields under short names, trades under full ones

	[JsonIgnore]
	public string Price => AggPrice ?? RawPrice;

	[JsonIgnore]
	public string Quantity => AggQuantity ?? RawQuantity;

	[JsonIgnore]
	public bool? IsBuyerMaker => AggIsBuyerMaker ?? RawIsBuyerMaker;
}
