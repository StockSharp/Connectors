namespace StockSharp.Bitget.Native.Futures.Model;

class Order
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instId")]
	public string InstId { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOid { get; set; }

	[JsonProperty("filledQty")]
	public double? FilledQty { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("force")]
	public string Force { get; set; }

	[JsonProperty("totalProfits")]
	public double? TotalProfits { get; set; }

	[JsonProperty("posSide")]
	public string PosSide { get; set; }

	[JsonProperty("marginCoin")]
	public string MarginCoin { get; set; }

	[JsonProperty("filledAmount")]
	public double? FilledAmount { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("leverage")]
	public double? Leverage { get; set; }

	[JsonProperty("marginMode")]
	public string MarginMode { get; set; }

	[JsonProperty("enterPointSource")]
	public string EnterPointSource { get; set; }

	[JsonProperty("tradeSide")]
	public string TradeSide { get; set; }

	[JsonProperty("posMode")]
	public string PosMode { get; set; }

	[JsonProperty("orderSource")]
	public string OrderSource { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }

	[JsonProperty("presetTakeProfitPrice")]
	public double? PresetTakeProfitPrice { get; set; }

	[JsonProperty("presetStopLossPrice")]
	public double? PresetStopLossPrice { get; set; }

	[JsonProperty("quoteVolume")]
	public double? QuoteVolume { get; set; }

	[JsonProperty("baseVolume")]
	public double? BaseVolume { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("remainSize")]
	public double? RemainSize { get; set; }
}

class OrderResponse
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("clientOid")]
	public string ClientOid { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("size")]
	public double? Size { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("force")]
	public string Force { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? UpdateTime { get; set; }
}
