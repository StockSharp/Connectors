namespace StockSharp.PolygonIO.Native.Model;

abstract class SocketBase
{
	[JsonProperty("ev")]
	public string EventType { get; set; }

	[JsonProperty("sym")]
	public string Symbol { get; set; }
}
