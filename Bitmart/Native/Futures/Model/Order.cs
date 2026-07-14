namespace StockSharp.Bitmart.Native.Futures.Model;

class Order
{
	// Order id
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	// Trading pair (e.g. BTC_USDT)
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Client-defined OrderId
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	// Side
	// 1=buy_open_long
	// 2=buy_close_short
	// 3=sell_close_long
	// 4=sell_open_short
	[JsonProperty("side")]
	public int Side { get; set; }

	// Order type
	// limit
	// market
	// trailing
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	// Open type
	// cross
	// isolated
	[JsonProperty("open_type")]
	public string OpenType { get; set; }

	[JsonProperty("deal_avg_price")]
	public double? DealAvgPrice { get; set; }

	[JsonProperty("deal_size")]
	public double? DealSize { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("activation_price")]
	public double? ActivationPrice { get; set; }

	[JsonProperty("callback_rate")]
	public double? CallbackRate { get; set; }

	[JsonProperty("activation_price_type")]
	public int? ActivationPriceType { get; set; }

	// Timestamp, accurate to milliseconds
	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("update_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }
}

class SocketOrderData
{
	// Action
	// 1=match deal
	// 2=submit order
	// 3=cancel order
	// 4=liquidate cancel order
	// 5=adl cancel order
	// 6=part liquidate
	// 7=bankruptcy order
	// 8=passive adl match deal
	// 9=active adl match deal
	[JsonProperty("action")]
	public int Action { get; set; }

	[JsonProperty("order")]
	public SocketOrder Order { get; set; }
}

class SocketOrder
{
	// Order id
	[JsonProperty("order_id")]
	public long OrderId { get; set; }

	// Trading pair (e.g. BTC_USDT)
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	// Client-defined OrderId
	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; set; }

	// Side
	// 1=buy_open_long
	// 2=buy_close_short
	// 3=sell_close_long
	// 4=sell_open_short
	[JsonProperty("side")]
	public int Side { get; set; }

	// Order type
	// limit
	// market
	// trailing
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("size")]
	public double Size { get; set; }

	[JsonProperty("leverage")]
	public int? Leverage { get; set; }

	// Open type
	// cross
	// isolated
	[JsonProperty("open_type")]
	public string OpenType { get; set; }

	[JsonProperty("deal_avg_price")]
	public double? DealAvgPrice { get; set; }

	[JsonProperty("deal_size")]
	public double? DealSize { get; set; }

	[JsonProperty("price")]
	public double? Price { get; set; }

	[JsonProperty("activation_price")]
	public double? ActivationPrice { get; set; }

	[JsonProperty("callback_rate")]
	public double? CallbackRate { get; set; }

	[JsonProperty("activation_price_type")]
	public int? ActivationPriceType { get; set; }

	// Timestamp, accurate to milliseconds
	[JsonProperty("create_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime CreateTime { get; set; }

	[JsonProperty("update_time")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime UpdateTime { get; set; }

	[JsonProperty("last_trade")]
	public SocketTrade LastTrade { get; set; }
}

class SocketTrade
{
	[JsonProperty("lastTradeID")]
	public long LastTradeId { get; set; }

	[JsonProperty("fillQty")]
	public double FillQty { get; set; }

	[JsonProperty("fillPrice")]
	public double FillPrice { get; set; }

	[JsonProperty("fee")]
	public double? Fee { get; set; }

	[JsonProperty("feeCcy")]
	public string FeeCcy { get; set; }
}