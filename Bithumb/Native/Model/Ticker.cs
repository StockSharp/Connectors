namespace StockSharp.Bithumb.Native.Model;

sealed class Ticker
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("opening_price")]
	public decimal? OpeningPrice { get; set; }

	[JsonProperty("high_price")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low_price")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("trade_price")]
	public decimal? TradePrice { get; set; }

	[JsonProperty("change_price")]
	public decimal? ChangePrice { get; set; }

	[JsonProperty("trade_volume")]
	public decimal? TradeVolume { get; set; }

	[JsonProperty("acc_trade_volume_24h")]
	public decimal? AccumulatedVolume24H { get; set; }

	[JsonProperty("trade_timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeTimestamp { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("ask_bid")]
	public string AskBid { get; set; }

	[JsonIgnore]
	public string Symbol => Code.IsEmpty() ? Market : Code;
}
