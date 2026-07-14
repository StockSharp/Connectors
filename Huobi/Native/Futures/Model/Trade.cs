namespace StockSharp.Huobi.Native.Futures.Model;

class TradeBase
{
	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}

class Trade : TradeBase
{
	[JsonProperty("trade-id")]
	public long TradeId { get; set; }
}

class SocketTrade : TradeBase
{
	[JsonProperty("tradeId")]
	public long TradeId { get; set; }
}