namespace StockSharp.Bitmart.Native.Spot.Model;

class OwnTrade
{
	// Trade id
	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	// Order id
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	// User-defined ID
	[JsonProperty("clientOrderId")]
	public string ÑlientOrderId { get; set; }

	// Trading pair symbol
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Side
	[JsonProperty("side")]
	public string Side { get; set; }

	// Order mode
	// spot			= spot
	// iso_margin	= isolated margin
	[JsonProperty("orderMode")]
	public string OrderMode { get; set; }

	// Order type
	// limit		=limit order
	// market		=market order
	// limit_maker	=PostOnly order
	// ioc			=IOC order
	[JsonProperty("type")]
	public string Type { get; set; }

	// Order size
	[JsonProperty("price")]
	public double? Price { get; set; }

	// Order size
	[JsonProperty("size")]
	public double? Size { get; set; }

	// Notional amount
	[JsonProperty("notional")]
	public double? Notional { get; set; }

	// Fee
	[JsonProperty("fee")]
	public double? Fee { get; set; }

	// Coin used for paying fees
	[JsonProperty("feeCoinName")]
	public string FeeCoinName { get; set; }

	// rade role
	// taker	=Take orders, take the initiative to deal
	// maker	=Pending order, passive transaction
	[JsonProperty("tradeRole")]
	public string TradeRole { get; set; }

	// Trade time (in milliseconds)
	[JsonProperty("createTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	// Trade time (in milliseconds)
	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }
}