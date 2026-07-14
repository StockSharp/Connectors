namespace StockSharp.Alor.Native.Model;

class Position
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("brokerSymbol")]
	public string BrokerSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("portfolio")]
	public string Portfolio { get; set; }

	[JsonProperty("avgPrice")]
	public double? AvgPrice { get; set; }

	[JsonProperty("qtyUnits")]
	public double? QtyUnits { get; set; }

	[JsonProperty("openUnits")]
	public double? OpenUnits { get; set; }

	[JsonProperty("lotSize")]
	public double? LotSize { get; set; }

	[JsonProperty("volume")]
	public double? Volume { get; set; }

	[JsonProperty("shortName")]
	public string ShortName { get; set; }

	[JsonProperty("qtyT0")]
	public double? QtyT0 { get; set; }

	[JsonProperty("qtyT1")]
	public double? QtyT1 { get; set; }

	[JsonProperty("qtyT2")]
	public double? QtyT2 { get; set; }

	[JsonProperty("qtyTFuture")]
	public double? QtyTFuture { get; set; }

	[JsonProperty("qtyT0Batch")]
	public double? QtyT0Batch { get; set; }

	[JsonProperty("qtyT1Batch")]
	public double? QtyT1Batch { get; set; }

	[JsonProperty("qtyT2Batch")]
	public double? QtyT2Batch { get; set; }

	[JsonProperty("qtyTFutureBatch")]
	public double? QtyTFutureBatch { get; set; }

	[JsonProperty("qtyBatch")]
	public double? QtyBatch { get; set; }

	[JsonProperty("openQtyBatch")]
	public double? OpenQtyBatch { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("dailyUnrealisedPl")]
	public double? DailyUnrealisedPl { get; set; }

	[JsonProperty("unrealisedPl")]
	public double? UnrealisedPl { get; set; }

	[JsonProperty("isCurrency")]
	public bool IsCurrency { get; set; }
}