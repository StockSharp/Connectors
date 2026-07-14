namespace StockSharp.Huobi.Native.Spot.Model;

[JsonConverter(typeof(JArrayToObjectConverter))]
class OrderBookEntry
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class OrderBook
{
	[JsonProperty("bids")]
	public OrderBookEntry[] Bids { get; set; }

	[JsonProperty("asks")]
	public OrderBookEntry[] Asks { get; set; }

	[JsonProperty("seqNum")]
	public long SeqNum { get; set; }

	[JsonProperty("prevSeqNum")]
	public long PrevSeqNum { get; set; }
}