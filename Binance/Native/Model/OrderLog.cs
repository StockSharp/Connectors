namespace StockSharp.Binance.Native.Model;

class FutOrderLogItem
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("S")]
	public string Side { get; set; }

	[JsonProperty("o")]
	public string Type { get; set; }

	[JsonProperty("f")]
	public string Tif { get; set; }

	[JsonProperty("q")]
	public double? Quantity { get; set; }

	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("ap")]
	public double? AveragePrice { get; set; }

	[JsonProperty("X")]
	public string Status { get; set; }

	[JsonProperty("l")]
	public double? LastTradeSize { get; set; }

	[JsonProperty("z")]
	public double? AccumFilled { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? TradeTime { get; set; }
}

class FutOrderLog : BaseEvent
{
	[JsonProperty("o")]
	public FutOrderLogItem Order { get; set; }
}