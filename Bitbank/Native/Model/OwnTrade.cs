namespace StockSharp.Bitbank.Native.Model;

class OwnTrade
{
	[JsonProperty("trade_id")]
	public long Id { get; set; }

	[JsonProperty("pair")]
	public string Pair { get; set; }

	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("amount")]
	public double? Amount { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("maker_taker")]
	public string MakerTaker { get; set; }

	[JsonProperty("fee_amount_base")]
	public double? FeeAmountBase { get; set; }

	[JsonProperty("fee_amount_quote")]
	public double? FeeAmountQuote { get; set; }

	[JsonProperty("executed_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime ExecutedAt { get; set; }
}