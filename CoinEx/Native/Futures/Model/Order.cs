namespace StockSharp.CoinEx.Native.Futures.Model;

class Order
{
	[JsonProperty("order_id")]
	public long Id { get; set; }

	[JsonProperty("market")]
	public string Symbol { get; set; }

	[JsonProperty("market_type")]
	public string MarketType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("amount")]
	public double? Volume { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("unfilled_amount")]
	public double? Left { get; set; }

	[JsonProperty("filled_amount")]
	public double? FilledAmount { get; set; }

	[JsonProperty("filled_value")]
	public double? FilledValue { get; set; }

	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("fee_ccy")]
	public string FeeCurrency { get; set; }

	[JsonProperty("maker_fee_rate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("taker_fee_rate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("last_filled_amount")]
	public double? LastFilledAmount { get; set; }

	[JsonProperty("last_filled_price")]
	public double? LastFilledPrice { get; set; }

	[JsonProperty("realized_pnl")]
	public double? RealizedPnl { get; set; }

	[JsonProperty("created_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreatedAt { get; set; }

	[JsonProperty("updated_at")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdatedAt { get; set; }
}