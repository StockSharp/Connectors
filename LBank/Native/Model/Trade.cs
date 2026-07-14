namespace StockSharp.LBank.Native.Model;

class Trade
{
	[JsonProperty("tid")]
	public string Id { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("date_ms")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

class SocketTrade
{
	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("amount")]
	public double Amount { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("direction")]
	public string Direction { get; set; }

	[JsonProperty("TS")]
	public DateTime Time { get; set; }
}