namespace StockSharp.Bithumb.Native.Model;

sealed class Transaction
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("trade_price")]
	public decimal Price { get; set; }

	[JsonProperty("trade_volume")]
	public decimal Amount { get; set; }

	[JsonProperty("ask_bid")]
	public string Side { get; set; }

	[JsonProperty("sequential_id")]
	public long Id { get; set; }

	[JsonProperty("trade_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeTimestamp { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonIgnore]
	public string Symbol => Code.IsEmpty() ? Market : Code;

	[JsonIgnore]
	public DateTime Time => TradeTimestamp ?? Timestamp;
}
