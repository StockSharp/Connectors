namespace StockSharp.Bitget.Native.Futures.Model;

class UserTrade
{
	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("fillAmount")]
	public double? FillAmount { get; set; }

	[JsonProperty("profit")]
	public double? Profit { get; set; }

	[JsonProperty("enterPointSource")]
	public string EnterPointSource { get; set; }

	[JsonProperty("tradeSide")]
	public string TradeSide { get; set; }

	[JsonProperty("posMode")]
	public string PosMode { get; set; }

	[JsonProperty("tradeScope")]
	public string TradeScope { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }

	[JsonProperty("fillTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? FillTime { get; set; }

	[JsonProperty("feeCoin")]
	public string FeeCoin { get; set; }
}
