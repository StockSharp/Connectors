namespace StockSharp.Aster.Native.Common.Model;

class OrderBookSnapshot
{
	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public string[][] Bids { get; set; }

	[JsonProperty("asks")]
	public string[][] Asks { get; set; }
}
