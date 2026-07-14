namespace StockSharp.DXtrade.Native.Model;

class MarketDataRequest
{
	[JsonProperty("symbols")]
	public string[] Symbols { get; set; }

	[JsonProperty("account")]
	public string Account { get; set; }

	[JsonProperty("eventTypes")]
	public MarketDataEventType[] EventTypes { get; set; }
}

class MarketDataEventType
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("format")]
	public string Format { get; set; }

	[JsonProperty("candleType")]
	public string CandleType { get; set; }

	[JsonProperty("fromTime")]
	public object FromTime { get; set; }

	[JsonProperty("toTime")]
	public object ToTime { get; set; }

	[JsonProperty("count")]
	public long? Count { get; set; }
}
