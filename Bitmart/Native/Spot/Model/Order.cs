namespace StockSharp.Bitmart.Native.Spot.Model;

class Order
{
	// Order id
	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	// Client-defined OrderId
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	// Trading pair (e.g. BTC_USDT)
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

	// Order status
	// new				= The order has been accepted by the engine.
	// partially_filled	= a part of the order has been filled.
	[JsonProperty("state")]
	public string State { get; set; }

	// Order price
	[JsonProperty("price")]
	public double? Price { get; set; }

	// Average filled price
	[JsonProperty("priceAvg")]
	public double? PriceAvg { get; set; }

	// Order size (Base currency)
	[JsonProperty("size")]
	public double? Size { get; set; }

	// Trade amount, unit is quote currency (special case: base currency when selling market orders)
	[JsonProperty("notional")]
	public string Notional { get; set; }

	// Filled notional amount
	[JsonProperty("filledNotional")]
	public double? FilledNotional { get; set; }

	// Filled amount
	[JsonProperty("filledSize")]
	public double? FilledSize { get; set; }

	// Timestamp, accurate to milliseconds
	[JsonProperty("createTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	// Timestamp, accurate to milliseconds
	[JsonProperty("updateTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }
}

class SocketOrder
{
	// Order id
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	// Trading pair (e.g. BTC_USDT)
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Order price
	[JsonProperty("price")]
	public double? Price { get; set; }

	// Order size (Base currency)
	[JsonProperty("size")]
	public double? Size { get; set; }

	// Trade amount, unit is quote currency (special case: base currency when selling market orders)
	[JsonProperty("notional")]
	public double? Notional { get; set; }

	// Side
	[JsonProperty("side")]
	public string Side { get; set; }

	// Order type
	// limit
	// market
	[JsonProperty("type")]
	public string Type { get; set; }

	// Timestamp, accurate to milliseconds
	[JsonProperty("ms_t")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Timestamp { get; set; }

	// Filled amount
	[JsonProperty("filled_size")]
	public double? FilledSize { get; set; }

	// Filled notional amount
	[JsonProperty("filled_notional")]
	public double? FilledNotional { get; set; }

	// 0：Spot order
	[JsonProperty("margin_trading")]
	public string MarginTrading { get; set; }

	// Order type
	// 0=Regular
	// 1=Maker only(Post only)
	// 2=Fill or kill(FOK)
	// 3=Immediate or Cancel(IOC)
	[JsonProperty("order_type")]
	public int OrderType { get; set; }

	// Order state
	// 4=Order success, Pending for fulfilment
	// 5=Partially filled
	// 6=Fully filled
	// 8=Canceled
	// 12=Canceled after Partially filled
	[JsonProperty("state")]
	public int State { get; set; }

	// last fill price
	[JsonProperty("last_fill_price")]
	public double? LastFillPrice { get; set; }

	// last fill size
	[JsonProperty("last_fill_count")]
	public double? LastFillSize { get; set; }

	// last fill time
	[JsonProperty("last_fill_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? LastFillTime { get; set; }

	// Whether the trade was created by a maker or a taker
	[JsonProperty("exec_type")]
	public string IsMakerOrTaker { get; set; }

	// Trade id
	[JsonProperty("detail_id")]
	public long? DetailId { get; set; }

	// Client-defined OrderId
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }
}