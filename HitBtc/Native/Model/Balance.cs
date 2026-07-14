namespace StockSharp.HitBtc.Native.Model;

class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public double Available { get; set; }

	[JsonProperty("reserved")]
	public double Reserved { get; set; }
}