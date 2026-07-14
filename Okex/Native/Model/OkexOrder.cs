namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
class OkexOrder : IOkexTransaction
{
	[JsonProperty("instType")]
	public string InstType { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("ccy")]
	public string Ccy { get; set; }

	[JsonProperty("ordId")]
	public string Id { get; set; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("px")]
	public decimal? Price { get; set; }

	[JsonProperty("sz")]
	public decimal? Size { get; set; }

	[JsonProperty("notionalUsd")]
	public decimal? NotionalUsd { get; set; }

	[JsonProperty("fillNotionalUsd")]
	public decimal? FillNotionalUsd { get; set; }

	[JsonProperty("pnl")]
	public decimal? PnL { get; set; }

	[JsonProperty("ordType")]
	public string OrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("posSide")]
	public string PosSide { get; set; }

	[JsonProperty("tdMode")]
	public string TdMode { get; set; }

	[JsonProperty("accFillSz")]
	public decimal? AccumulatedFilledSize { get; set; }

	[JsonProperty("fillPx")]
	public decimal? LastFillPrice { get; set; }

	[JsonProperty("tradeId")]
	public long? LastTradeId { get; set; }

	[JsonProperty("fillSz")]
	public decimal? LastFillSize { get; set; }

	[JsonProperty("fillTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LastFillTime { get; set; }

	[JsonProperty("fillFee")]
	public decimal? LastFillFee { get; set; }

	[JsonProperty("fillFeeCcy")]
	public string LastFillFeeCcy { get; set; }

	[JsonProperty("execType")]
	public string ExecType { get; set; } // T: taker, M: maker

	[JsonProperty("avgPx")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("lever")]
	public decimal? Leverage { get; set; }

	[JsonProperty("tpTriggerPx")]
	public decimal? TpTriggerPrice { get; set; }

	[JsonProperty("tpOrdPx")]
	public decimal? TpOrdPrice { get; set; }

	[JsonProperty("slTriggerPx")]
	public decimal? SlTriggerPrice { get; set; }

	[JsonProperty("slOrdPx")]
	public decimal? SlOrdPrice { get; set; }

	[JsonProperty("feeCcy")]
	public string FeeCcy { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("rebateCcy")]
	public string RebateCcy { get; set; }

	[JsonProperty("rebate")]
	public decimal? Rebate { get; set; }

	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("uTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Timestamp { get; set; }

	[JsonProperty("cTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? CreatedAt { get; set; }

	[JsonProperty("reqId")]
	public string AmendReqId { get; set; }

	[JsonProperty("amendResult")]
	public string AmendResult { get; set; } // -1: fail, 0: success, 1: automatic cancel

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }

	[JsonProperty("reduceOnly")]
	public bool? ReduceOnly { get; set; }

	DateTime? IOkexTransaction.Time => Timestamp ?? CreatedAt;
}