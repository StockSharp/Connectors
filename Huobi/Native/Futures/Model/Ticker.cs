namespace StockSharp.Huobi.Native.Futures.Model;

class Ticker
{
	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("open")]
	public double? Open { get; set; }

	[JsonProperty("close")]
	public double? Close { get; set; }

	[JsonProperty("high")]
	public double? High { get; set; }

	[JsonProperty("count")]
	public int? Count { get; set; }

	[JsonProperty("low")]
	public double? Low { get; set; }

	[JsonProperty("vol")]
	public double? Volume { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class BestQuote
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class Best
{
	[JsonProperty("bid")]
	public BestQuote Bid { get; set; }

	[JsonProperty("ask")]
	public BestQuote Ask { get; set; }

	[JsonProperty("seqId")]
	public long SeqId { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }
}