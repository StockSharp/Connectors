namespace StockSharp.Bitmart.Native.Spot.Model;

class Ticker
{
	// Trading pair, BTC_USDT
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Last trading price
	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	// 24-hour highest price
	[JsonProperty("high_24h")]
	public double? High24h { get; set; }

	// 24-hour lowest price
	[JsonProperty("low_24h")]
	public double? Low24h { get; set; }

	// 24-hour open price
	[JsonProperty("open_24h")]
	public double? Open24h { get; set; }

	// 24-hour volume in base currency
	[JsonProperty("base_volume_24h")]
	public double? BaseVolume24h { get; set; }

	// timestamp (in seconds)
	[JsonProperty("s_t")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime Timestamp { get; set; }
}