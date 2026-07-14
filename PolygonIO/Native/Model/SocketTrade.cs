namespace StockSharp.PolygonIO.Native.Model;

class SocketTrade : SocketBase
{
	[JsonProperty("x")]
	public int Exchange { get; set; }

	[JsonProperty("i")]
	public string Id { get; set; }

	[JsonProperty("z")]
	public int Tape { get; set; }

	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("s")]
	public double Size { get; set; }

	[JsonProperty("c")]
	public int[] Conditions { get; set; }

	[JsonProperty("t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("q")]
	public long SeqNum { get; set; }
}
