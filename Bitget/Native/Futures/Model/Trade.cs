namespace StockSharp.Bitget.Native.Futures.Model;

class Trade
{
	[JsonProperty("instId")]
	public string Symbol { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}
