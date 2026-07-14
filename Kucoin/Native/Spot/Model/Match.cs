namespace StockSharp.Kucoin.Native.Spot.Model;

class SocketMatch
{
	[JsonProperty("sequence")]
	public long Sequence { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("takerOrderId")]
	public string TakerOrderId { get; set; }

	[JsonProperty("makerOrderId")]
	public string MakerOrderId { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeNanoConverter))]
	public DateTime Time { get; set; }
}
