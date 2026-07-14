namespace StockSharp.Kraken.Native.Spot.Model;

class PositionInfo
{
	[JsonProperty("ordertxid")]
	public string OrderTransactionId { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("time")]
	public double Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("cost")]
	public decimal Cost { get; set; }

	[JsonProperty("fee")]
	public decimal Fee { get; set; }

	[JsonProperty("vol")]
	public decimal Volume { get; set; }

	[JsonProperty("vol_closed")]
	public decimal VolumeClosed { get; set; }

	[JsonProperty("margin")]
	public decimal Margin { get; set; }

	[JsonProperty("value")]
	public decimal Value { get; set; }

	[JsonProperty("net")]
	public decimal Net { get; set; }

	[JsonProperty("misc")]
	public string Misc { get; set; }

	[JsonProperty("oflags")]
	public string OrderFlags { get; set; }

	[JsonProperty("viqc")]
	public decimal VolumeInQuoteCurrency { get; set; }
}