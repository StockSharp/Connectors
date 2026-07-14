namespace StockSharp.Mexc.Native.Spot.Model;

class Trade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("qty")]
	public double? Qty { get; set; }

	[JsonProperty("quoteQty")]
	public double? QuoteQty { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("isBestMatch")]
	public bool IsBestMatch { get; set; }
}

class TradeStream
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Id { get; set; }

	[JsonProperty("p")]
	public double? Price { get; set; }

	[JsonProperty("q")]
	public double? Quantity { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}