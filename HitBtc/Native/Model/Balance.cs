namespace StockSharp.HitBtc.Native.Model;

class Balance
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("reserved")]
	public decimal Reserved { get; set; }
}
