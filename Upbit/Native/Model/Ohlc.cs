namespace StockSharp.Upbit.Native.Model;

class Ohlc
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("candle_date_time_utc")]
	public DateTime DateTimeUtc { get; set; }

	[JsonProperty("candle_date_time_kst")]
	public DateTime DateTimeKst { get; set; }

	[JsonProperty("opening_price")]
	public double Open { get; set; }

	[JsonProperty("high_price")]
	public double High { get; set; }

	[JsonProperty("low_price")]
	public double Low { get; set; }

	[JsonProperty("trade_price")]
	public double Close { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	[JsonProperty("candle_acc_trade_price")]
	public double CandleAccTradePrice { get; set; }

	[JsonProperty("candle_acc_trade_volume")]
	public double Volume { get; set; }

	[JsonProperty("unit")]
	public string Unit { get; set; }
}