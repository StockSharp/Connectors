namespace StockSharp.Deribit.Native.Model;

class Trade
{
	[JsonProperty("trade_id")]
	public string Id { get; set; }

	[JsonProperty("instrument_name")]
	public string Instrument { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TimeStamp { get; set; }

	[JsonProperty("tradeSeq")]
	public long TradeSeq { get; set; }

	[JsonProperty("amount")]
	public double Quantity { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("tick_direction")]
	public int TickDirection { get; set; }

	[JsonProperty("index_price")]
	public double? IndexPrice { get; set; }
}