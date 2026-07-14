namespace StockSharp.GateIO.Native.Options.Model;

class Symbol
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("expiration_time")]
	[JsonConverter(typeof(JsonDateTimeConverter))]
	public DateTime ExpirationTime { get; set; }

	[JsonProperty("is_call")]
	public bool IsCall { get; set; }

	[JsonProperty("strike_price")]
	public double? StrikePrice { get; set; }

	[JsonProperty("last_price")]
	public double? LastPrice { get; set; }

	[JsonProperty("mark_price")]
	public double? MarkPrice { get; set; }

	[JsonProperty("orderbook_id")]
	public long? OrderbookId { get; set; }

	[JsonProperty("trade_id")]
	public long? TradeId { get; set; }

	[JsonProperty("trade_size")]
	public double? TradeSize { get; set; }

	[JsonProperty("position_size")]
	public double? PositionSize { get; set; }

	[JsonProperty("underlying")]
	public string Underlying { get; set; }

	[JsonProperty("underlying_price")]
	public double? UnderlyingPrice { get; set; }

	[JsonProperty("multiplier")]
	public double? Multiplier { get; set; }

	[JsonProperty("order_price_round")]
	public double? OrderPriceRound { get; set; }

	[JsonProperty("mark_price_round")]
	public double? MarkPriceRound { get; set; }

	[JsonProperty("maker_fee_rate")]
	public double? MakerFeeRate { get; set; }

	[JsonProperty("taker_fee_rate")]
	public double? TakerFeeRate { get; set; }

	[JsonProperty("price_limit_fee_rate")]
	public double? PriceLimitFeeRate { get; set; }

	[JsonProperty("ref_discount_rate")]
	public double? RefDiscountRate { get; set; }

	[JsonProperty("ref_rebate_rate")]
	public double? RefRebateRate { get; set; }

	[JsonProperty("order_price_deviate")]
	public double? OrderPriceDeviate { get; set; }

	[JsonProperty("order_size_min")]
	public double? OrderSizeMin { get; set; }

	[JsonProperty("order_size_max")]
	public double? OrderSizeMax { get; set; }

	[JsonProperty("orders_limit")]
	public int? OrdersLimit { get; set; }
}