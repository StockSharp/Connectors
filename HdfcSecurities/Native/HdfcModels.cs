namespace StockSharp.HdfcSecurities.Native;

sealed class HdfcProfile
{
	[JsonProperty("user_id")]
	public string UserId { get; set; }

	[JsonProperty("user_name")]
	public string UserName { get; set; }

	[JsonProperty("broker")]
	public string Broker { get; set; }

	[JsonProperty("products")]
	public string[] Products { get; set; }

	[JsonProperty("order_types")]
	public string[] OrderTypes { get; set; }
}

sealed class HdfcInstrument
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("instrument_segment")]
	public string InstrumentSegment { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strike_price")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("lot_size")]
	public decimal LotSize { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("exch_security_id")]
	public string ExchangeSecurityId { get; set; }

	[JsonProperty("symbol_name")]
	public string SymbolName { get; set; }

	[JsonProperty("underline_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("open_price")]
	public decimal OpenPrice { get; set; }
}

sealed class HdfcLtp
{
	[JsonProperty("prev_close")]
	public decimal PreviousClose { get; set; }

	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class HdfcOrder
{
	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("status_message")]
	public string StatusMessage { get; set; }

	[JsonProperty("status_message_raw")]
	public string StatusMessageRaw { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("company_name")]
	public string CompanyName { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("instrument_segment")]
	public string InstrumentSegment { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosed_quantity")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("cancelled_quantity")]
	public decimal CancelledQuantity { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("total_traded_value")]
	public decimal TotalTradedValue { get; set; }

	[JsonProperty("external_reference_number")]
	public string ExternalReferenceNumber { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("token_id")]
	public string TokenId { get; set; }
}

sealed class HdfcTrade
{
	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("exchange_order_id")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("company_name")]
	public string CompanyName { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("instrument_segment")]
	public string InstrumentSegment { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("filled_quantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("fill_timestamp")]
	public string FillTimestamp { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }
}

sealed class HdfcPosition
{
	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("instrument_segment")]
	public string InstrumentSegment { get; set; }

	[JsonProperty("underlying_symbol")]
	public string UnderlyingSymbol { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("total_buy_quantity")]
	public decimal TotalBuyQuantity { get; set; }

	[JsonProperty("total_sell_qty")]
	public decimal TotalSellQuantity { get; set; }

	[JsonProperty("net_qty")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("average_buy_price")]
	public decimal AverageBuyPrice { get; set; }

	[JsonProperty("average_sell_price")]
	public decimal AverageSellPrice { get; set; }

	[JsonProperty("realised_pl_overall_position")]
	public decimal RealizedPnL { get; set; }
}

sealed class HdfcHolding
{
	[JsonProperty("security_id")]
	public string SecurityId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("company_name")]
	public string CompanyName { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("close_price")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("ltcg_quantity")]
	public decimal LongTermQuantity { get; set; }
}

sealed class HdfcMargins
{
	[JsonProperty("total_available_limit")]
	public decimal Available { get; set; }

	[JsonProperty("total_utilised_limit")]
	public decimal Utilized { get; set; }

	[JsonProperty("total_limit")]
	public decimal Total { get; set; }
}

sealed class HdfcDepthLevel
{
	public decimal Price { get; init; }
	public long Quantity { get; init; }
	public long Orders { get; init; }
	public bool IsBid { get; init; }
}

sealed class HdfcMarketUpdate
{
	public string StreamId { get; init; }
	public long InstrumentId { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal LastPrice { get; init; }
	public long LastQuantity { get; init; }
	public decimal OpenPrice { get; init; }
	public decimal HighPrice { get; init; }
	public decimal LowPrice { get; init; }
	public decimal PreviousClose { get; init; }
	public long Volume { get; init; }
	public decimal AveragePrice { get; init; }
	public long TotalBuyQuantity { get; init; }
	public long TotalSellQuantity { get; init; }
	public decimal LowerLimit { get; init; }
	public decimal UpperLimit { get; init; }
	public long OpenInterest { get; init; }
	public HdfcDepthLevel[] Depth { get; init; } = [];
}
