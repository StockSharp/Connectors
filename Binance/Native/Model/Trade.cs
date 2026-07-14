namespace StockSharp.Binance.Native.Model;

class Trade : BaseEvent
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Id { get; set; }

	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("q")]
	public double Quantity { get; set; }

	[JsonProperty("b")]
	public long Buyer { get; set; }

	[JsonProperty("a")]
	public long Seller { get; set; }

	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("m")]
	public bool IsMarketMaker { get; set; }

	[JsonProperty("M")]
	public bool Ignore { get; set; }

	[JsonProperty("X")]
	public string Source { get; set; }
}

class HttpTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("price")]
	public double Price { get; set; }

	[JsonProperty("qty")]
	public double Quantity { get; set; }

	[JsonProperty("time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("isBuyerMaker")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("isBestMatch")]
	public bool IsBestMatch { get; set; }
}
