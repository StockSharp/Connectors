namespace StockSharp.Bitget.Native.Spot.Model;

class Balance
{
	[JsonProperty("coin")]
	public string Coin { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("frozen")]
	public double? Frozen { get; set; }

	[JsonProperty("lock")]
	public double? Locked { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }
}
