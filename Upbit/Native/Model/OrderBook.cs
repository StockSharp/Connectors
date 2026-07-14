namespace StockSharp.Upbit.Native.Model;

class OrderBookEntry
{
	[JsonProperty("ask_price")]
	public double AskPrice { get; set; }

	[JsonProperty("bid_price")]
	public double BidPrice { get; set; }

	[JsonProperty("ask_size")]
	public double AskSize { get; set; }

	[JsonProperty("bid_size")]
	public double BidSize { get; set; }
}

class OrderBook : BaseEvent
{
	[JsonProperty("total_ask_size")]
	public double? TotalAskSize { get; set; }

	[JsonProperty("total_bid_size")]
	public double? TotalBidSize { get; set; }

	[JsonProperty("orderbook_units")]
	public OrderBookEntry[] Units { get; set; }
}