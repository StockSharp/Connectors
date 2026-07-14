namespace StockSharp.Okex.Native.Model;

[Obfuscation(Feature = "renaming", ApplyToMembers = false)]
class OwnTrade : IOkexTransaction
{
	[JsonProperty("instType")]
	public string InstType { get; set; }

	[JsonProperty("instId")]
	public string InstrumentId { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("ordId")]
	public string OrderId { get; set; }

	[JsonProperty("clOrdId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("billId")]
	public string BillId { get; set; }

	[JsonProperty("tag")]
	public string OrderTag { get; set; }

	[JsonProperty("fillPx")]
	public decimal Price { get; set; }

	[JsonProperty("fillSz")]
	public decimal Size { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("posSide")]
	public string PosSide { get; set; }

	[JsonProperty("execType")]
	public string Type { get; set; }

	[JsonProperty("feeCcy")]
	public string FeeCurrency { get; set; }

	[JsonProperty("fee")]
	public decimal? Fee { get; set; }

	[JsonProperty("ts")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? Time { get; set; }
}