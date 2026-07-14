namespace StockSharp.Binance.Native.Model;

class Ticker : BaseEvent
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public double? PriceChange { get; set; }

	[JsonProperty("P")]
	public double? PriceChangePercentage { get; set; }

	[JsonProperty("w")]
	public double? VWAP { get; set; }

	[JsonProperty("x")]
	public double? PrevClose { get; set; }

	[JsonProperty("c")]
	public double? CurrClose { get; set; }

	[JsonProperty("Q")]
	public double? CloseQuantity { get; set; }

	[JsonProperty("b")]
	public double? BestBidPrice { get; set; }

	[JsonProperty("B")]
	public double? BestBidQuantity { get; set; }

	[JsonProperty("a")]
	public double? BestAskPrice { get; set; }

	[JsonProperty("A")]
	public double? BestAskQuantity { get; set; }

	[JsonProperty("o")]
	public double? Open { get; set; }

	[JsonProperty("h")]
	public double? High { get; set; }

	[JsonProperty("l")]
	public double? Low { get; set; }

	[JsonProperty("v")]
	public double? AssetVolume { get; set; }

	[JsonProperty("q")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("O")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime StatisticsOpenTime { get; set; }

	[JsonProperty("C")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime StatisticsCloseTime { get; set; }

	[JsonProperty("F")]
	public long? FirstTradeId { get; set; }

	[JsonProperty("L")]
	public long? LastTradeId { get; set; }

	[JsonProperty("n")]
	public int? TradesCount { get; set; }
}