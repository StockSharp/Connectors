namespace StockSharp.Upbit.Native.Model;

class Trade : BaseEvent
{
	[JsonProperty("trade_date")]
	public string TradeDate { get; set; }

	[JsonProperty("trade_time")]
	public string TradeTime { get; set; }

	[JsonProperty("trade_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime TradeTimestamp { get; set; }

	[JsonProperty("trade_price")]
	public double TradePrice { get; set; }

	[JsonProperty("trade_volume")]
	public double TradeVolume { get; set; }

	[JsonProperty("ask_bid")]
	public string AskBid { get; set; }

	[JsonProperty("prev_closing_price")]
	public double? PrevClosingPrice { get; set; }

	[JsonProperty("change")]
	public string Change { get; set; }

	[JsonProperty("change_price")]
	public double? ChangePrice { get; set; }

	[JsonProperty("sequential_id")]
	public long SequentialId { get; set; }
}