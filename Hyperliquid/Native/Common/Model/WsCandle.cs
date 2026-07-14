namespace StockSharp.Hyperliquid.Native.Common.Model;

class WsCandle
{
	[JsonProperty("t")]
	public long T { get; set; }

	[JsonProperty("T")]
	public long TClose { get; set; }

	[JsonProperty("s")]
	public string S { get; set; }

	[JsonProperty("i")]
	public string I { get; set; }

	[JsonProperty("o")]
	public string O { get; set; }

	[JsonProperty("c")]
	public string C { get; set; }

	[JsonProperty("h")]
	public string H { get; set; }

	[JsonProperty("l")]
	public string L { get; set; }

	[JsonProperty("v")]
	public string V { get; set; }

	[JsonProperty("n")]
	public int N { get; set; }
}
