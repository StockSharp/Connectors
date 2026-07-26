namespace StockSharp.HitBtc.Native.Model;

class Ohlc
{
	[JsonProperty("timestamp")]
	public DateTime Time { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("max")]
	public decimal High { get; set; }

	[JsonProperty("min")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("volume_quote")]
	public decimal VolumeQuote { get; set; }
}

class WsOhlc
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("q")]
	public decimal VolumeQuote { get; set; }

	public Ohlc ToOhlc()
		=> new()
		{
			Time = Timestamp.FromHitBtcMilliseconds(),
			Open = Open,
			High = High,
			Low = Low,
			Close = Close,
			Volume = Volume,
			VolumeQuote = VolumeQuote,
		};
}
