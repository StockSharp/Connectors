namespace StockSharp.PolygonIO.Native.Model;

class SocketQuote : SocketBase
{
	[JsonProperty("bx")]
	public int BidExchange { get; set; }

	[JsonProperty("bp")]
	public double BidPrice { get; set; }

	[JsonProperty("bs")]
	public double BidSize { get; set; }

	[JsonProperty("ax")]
	public int AskExchange { get; set; }

	[JsonProperty("ap")]
	public double AskPrice { get; set; }

	[JsonProperty("as")]
	public double AskSize { get; set; }

	[JsonProperty("c")]
	public int Condition { get; set; }

	[JsonProperty("i")]
	public int[] Indicators { get; set; }

	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("q")]
	public long SeqNum { get; set; }

	[JsonProperty("z")]
	public int Tape { get; set; }
}
