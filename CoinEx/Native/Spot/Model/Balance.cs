namespace StockSharp.CoinEx.Native.Spot.Model;

class Balance
{
	[JsonProperty("margin_market")]
	public string MarginMarket { get; set; }

	[JsonProperty("ccy")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public double? Available { get; set; }

	[JsonProperty("frozen")]
	public double? Frozen { get; set; }

	[JsonProperty("updated_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdatedAt { get; set; }
}