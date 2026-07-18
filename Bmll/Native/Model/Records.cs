namespace StockSharp.Bmll.Native.Model;

sealed class BmllMarketDataRecord
{
	[JsonProperty("ExchangeTicker")]
	public string ExchangeTicker { get; set; }

	[JsonProperty("Ticker")]
	public string Ticker { get; set; }

	[JsonProperty("ISOExchangeCode")]
	public string IsoExchangeCode { get; set; }

	[JsonProperty("MIC")]
	public string Mic { get; set; }

	[JsonProperty("ExecutionVenue")]
	public string ExecutionVenue { get; set; }

	[JsonProperty("ListingId")]
	public long? ListingId { get; set; }

	[JsonProperty("InstrumentId")]
	public long? InstrumentId { get; set; }

	[JsonProperty("BMLLObjectId")]
	public long? BmllObjectId { get; set; }

	[JsonProperty("TradeDate")]
	public string TradeDate { get; set; }

	[JsonProperty("TradeTimestamp")]
	public string TradeTimestamp { get; set; }

	[JsonProperty("PublicationTimestamp")]
	public string PublicationTimestamp { get; set; }

	[JsonProperty("TradeTimestampNanoseconds")]
	public long? TradeTimestampNanoseconds { get; set; }

	[JsonProperty("PublicationTimestampNanoseconds")]
	public long? PublicationTimestampNanoseconds { get; set; }

	[JsonProperty("TimestampNanoseconds")]
	public long? TimestampNanoseconds { get; set; }

	[JsonProperty("ReceiveTimestampNanoseconds")]
	public long? ReceiveTimestampNanoseconds { get; set; }

	[JsonProperty("SequenceNo")]
	public long? SequenceNo { get; set; }

	[JsonProperty("BMLLSequenceNo")]
	public long? BmllSequenceNo { get; set; }

	[JsonProperty("BMLLSequenceSource")]
	public long? BmllSequenceSource { get; set; }

	[JsonProperty("ExchangeSequenceNo")]
	public long? ExchangeSequenceNo { get; set; }

	[JsonProperty("EventNo")]
	public long? EventNo { get; set; }

	[JsonProperty("TradeId")]
	public string TradeId { get; set; }

	[JsonProperty("AggressorSide")]
	public BmllAggressorSides? AggressorSide { get; set; }

	[JsonProperty("Price")]
	public decimal? Price { get; set; }

	[JsonProperty("Size")]
	public decimal? Size { get; set; }

	[JsonProperty("Printable")]
	public bool? IsPrintable { get; set; }

	[JsonProperty("ModificationIndicator")]
	public string ModificationIndicator { get; set; }

	[JsonProperty("Side")]
	public BmllSides? Side { get; set; }

	[JsonProperty("LobAction")]
	public BmllLobActions? LobAction { get; set; }

	[JsonProperty("OriginalOrderId")]
	public string OriginalOrderId { get; set; }

	[JsonProperty("OrderId")]
	public string OrderId { get; set; }

	[JsonProperty("OldOrderId")]
	public string OldOrderId { get; set; }

	[JsonProperty("OldPrice")]
	public decimal? OldPrice { get; set; }

	[JsonProperty("OldSize")]
	public decimal? OldSize { get; set; }

	[JsonProperty("OrderExecuted")]
	public bool? IsOrderExecuted { get; set; }

	[JsonProperty("ExecutionPrice")]
	public decimal? ExecutionPrice { get; set; }

	[JsonProperty("ExecutionSize")]
	public decimal? ExecutionSize { get; set; }

	[JsonProperty("PriceLevel")]
	public int? PriceLevel { get; set; }

	[JsonProperty("OldPriceLevel")]
	public int? OldPriceLevel { get; set; }

	[JsonProperty("SizeAhead")]
	public long? SizeAhead { get; set; }

	[JsonProperty("OrdersAhead")]
	public long? OrdersAhead { get; set; }

	[JsonProperty("PosChange")]
	public bool? IsPositionChanged { get; set; }

	[JsonProperty("EndOfEvent")]
	public bool? IsEndOfEvent { get; set; }

	[JsonProperty("MarketState")]
	public string MarketState { get; set; }

	[JsonProperty("DelOrderIndex")]
	public int? DeleteOrderIndex { get; set; }

	[JsonProperty("AddOrderIndex")]
	public int? AddOrderIndex { get; set; }

	[JsonProperty("MPIDAttribution")]
	public string MpidAttribution { get; set; }

	[JsonProperty("OrderType")]
	public BmllOrderTypes? OrderType { get; set; }

	[JsonProperty("OriginalExchangeMessage")]
	public string OriginalExchangeMessage { get; set; }
}
