namespace StockSharp.Groww.Native.Model;

internal sealed class GrowwPlaceOrderRequest
{
	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("trigger_price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("order_reference_id")]
	public string OrderReferenceId { get; set; }
}

internal sealed class GrowwModifyOrderRequest
{
	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("trigger_price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("groww_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }
}

internal sealed class GrowwCancelOrderRequest
{
	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("groww_order_id")]
	public string OrderId { get; set; }
}

internal sealed class GrowwOrderResult
{
	[JsonProperty("groww_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("order_status")]
	public string Status { get; set; }

	[JsonProperty("order_reference_id")]
	public string OrderReferenceId { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }
}

internal sealed class GrowwOrderListPayload
{
	[JsonProperty("order_list")]
	public GrowwOrder[] Orders { get; set; }
}

internal sealed class GrowwOrder
{
	[JsonProperty("groww_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("order_status")]
	public string Status { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("trigger_price")]
	public decimal? TriggerPrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal? FilledQuantity { get; set; }

	[JsonProperty("remaining_quantity")]
	public decimal? RemainingQuantity { get; set; }

	[JsonProperty("average_fill_price")]
	public decimal? AverageFillPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("exchange_time")]
	public string ExchangeTime { get; set; }

	[JsonProperty("trade_date")]
	public string TradeDate { get; set; }

	[JsonProperty("order_reference_id")]
	public string OrderReferenceId { get; set; }
}

internal sealed class GrowwTradeListPayload
{
	[JsonProperty("trade_list")]
	public GrowwTrade[] Trades { get; set; }
}

internal sealed class GrowwTrade
{
	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("groww_order_id")]
	public string OrderId { get; set; }

	[JsonProperty("groww_trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("exchange_trade_id")]
	public string ExchangeTradeId { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("trade_status")]
	public string Status { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("remark")]
	public string Remark { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("created_at")]
	public string CreatedAt { get; set; }

	[JsonProperty("trade_date_time")]
	public string TradeDateTime { get; set; }
}

internal sealed class GrowwHoldingsPayload
{
	[JsonProperty("holdings")]
	public GrowwHolding[] Holdings { get; set; }
}

internal sealed class GrowwHolding
{
	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("average_price")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("pledge_quantity")]
	public decimal? PledgeQuantity { get; set; }

	[JsonProperty("t1_quantity")]
	public decimal? T1Quantity { get; set; }
}

internal sealed class GrowwPositionsPayload
{
	[JsonProperty("positions")]
	public GrowwPosition[] Positions { get; set; }
}

internal sealed class GrowwPosition
{
	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("credit_quantity")]
	public decimal? CreditQuantity { get; set; }

	[JsonProperty("credit_price")]
	public decimal? CreditPrice { get; set; }

	[JsonProperty("debit_quantity")]
	public decimal? DebitQuantity { get; set; }

	[JsonProperty("debit_price")]
	public decimal? DebitPrice { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symbol_isin")]
	public string Isin { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("net_price")]
	public decimal? NetPrice { get; set; }

	[JsonProperty("realised_pnl")]
	public decimal? RealizedPnL { get; set; }
}
