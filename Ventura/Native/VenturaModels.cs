namespace StockSharp.Ventura.Native;

sealed class VenturaAuthResult
{
	[JsonProperty("client_id")]
	public string ClientId { get; set; }

	[JsonProperty("auth_token")]
	public string AuthToken { get; set; }

	[JsonProperty("auth_expiry")]
	public string AuthExpiry { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("refresh_expiry")]
	public string RefreshExpiry { get; set; }
}

sealed class VenturaInstrument
{
	[JsonProperty("exchange_token")]
	public string ExchangeToken { get; set; }

	[JsonProperty("trading_symbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("last_price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("strike")]
	public decimal Strike { get; set; }

	[JsonProperty("tick_size")]
	public decimal TickSize { get; set; }

	[JsonProperty("lot_size")]
	public decimal LotSize { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }
}

sealed class VenturaDepthLevel
{
	public decimal BuyQuantity { get; init; }
	public decimal SellQuantity { get; init; }
	public long BuyOrders { get; init; }
	public long SellOrders { get; init; }
	public decimal BuyPrice { get; init; }
	public decimal SellPrice { get; init; }
}

sealed class VenturaMarketUpdate
{
	public string Action { get; init; }
	public string Token { get; init; }
	public DateTime ServerTime { get; init; }
	public decimal LastPrice { get; init; }
	public decimal OpenPrice { get; init; }
	public decimal HighPrice { get; init; }
	public decimal LowPrice { get; init; }
	public decimal PreviousClose { get; init; }
	public decimal Volume { get; init; }
	public decimal UpperCircuit { get; init; }
	public decimal LowerCircuit { get; init; }
	public decimal TotalBuyQuantity { get; init; }
	public decimal TotalSellQuantity { get; init; }
	public VenturaDepthLevel[] Depth { get; init; } = [];
}

sealed class VenturaOrder
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("last_traded_price")]
	public decimal LastTradedPrice { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("product_type")]
	public string ProductType { get; set; }

	[JsonProperty("order_type")]
	public JToken OrderType { get; set; }

	[JsonProperty("average_traded_price")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("trigger_price")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("total_quantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("pending_quantity")]
	public decimal PendingQuantity { get; set; }

	[JsonProperty("executed_quantity")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("order_date_time")]
	public string OrderDateTime { get; set; }

	[JsonProperty("reason")]
	public string Reason { get; set; }

	[JsonProperty("disclosed_quantity_remaining")]
	public decimal DisclosedQuantityRemaining { get; set; }

	[JsonProperty("validity")]
	public JToken Validity { get; set; }

	[JsonProperty("lot_size")]
	public decimal LotSize { get; set; }
}

sealed class VenturaTrade
{
	[JsonProperty("order_id")]
	public string OrderId { get; set; }

	[JsonProperty("trade_id")]
	public string TradeId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("order_type")]
	public string OrderType { get; set; }

	[JsonProperty("transaction_type")]
	public string TransactionType { get; set; }

	[JsonProperty("product_type")]
	public string ProductType { get; set; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("filled_timestamp")]
	public string FilledTimestamp { get; set; }

	[JsonProperty("order_timestamp")]
	public string OrderTimestamp { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }
}

sealed class VenturaPosition
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("last_traded_price")]
	public decimal LastTradedPrice { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("product_type")]
	public string ProductType { get; set; }

	[JsonProperty("average_traded_price")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("total_quantity")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("profit_loss")]
	public decimal ProfitLoss { get; set; }

	[JsonProperty("lot_size")]
	public decimal LotSize { get; set; }

	[JsonProperty("instrument_type")]
	public string InstrumentType { get; set; }

	[JsonProperty("expiry_date")]
	public string ExpiryDate { get; set; }

	[JsonProperty("option_type")]
	public string OptionType { get; set; }

	[JsonProperty("strike_price")]
	public decimal StrikePrice { get; set; }
}

sealed class VenturaHolding
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("average_traded_price")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("investment_value")]
	public decimal InvestmentValue { get; set; }

	[JsonProperty("last_traded_price")]
	public decimal LastTradedPrice { get; set; }

	[JsonProperty("current_value")]
	public decimal CurrentValue { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("profit_loss")]
	public decimal ProfitLoss { get; set; }

	[JsonProperty("mtf_quantity")]
	public decimal MtfQuantity { get; set; }
}

sealed class VenturaFunds
{
	[JsonProperty("available_to_trade")]
	public decimal AvailableToTrade { get; set; }

	[JsonProperty("withdrawable_balance")]
	public decimal WithdrawableBalance { get; set; }

	[JsonProperty("net_option_premium")]
	public decimal NetOptionPremium { get; set; }

	[JsonProperty("total_margin")]
	public VenturaMargin TotalMargin { get; set; }

	[JsonProperty("receivable_funds")]
	public decimal ReceivableFunds { get; set; }

	[JsonProperty("utilised_margin")]
	public VenturaUtilizedMargin UtilizedMargin { get; set; }
}

sealed class VenturaMargin
{
	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("ledger_balance")]
	public decimal LedgerBalance { get; set; }

	[JsonProperty("pledge_margin")]
	public decimal PledgeMargin { get; set; }

	[JsonProperty("pay_in")]
	public decimal PayIn { get; set; }
}

sealed class VenturaUtilizedMargin
{
	[JsonProperty("total")]
	public decimal Total { get; set; }

	[JsonProperty("pending_order_margin")]
	public decimal PendingOrderMargin { get; set; }

	[JsonProperty("position_margin")]
	public decimal PositionMargin { get; set; }

	[JsonProperty("booked_loss")]
	public decimal BookedLoss { get; set; }
}

sealed class VenturaOrderStatusUpdate
{
	public string Message { get; init; }
	public string SecurityId { get; init; }
	public string OrderId { get; init; }
	public decimal TradedQuantity { get; init; }
	public decimal TotalQuantity { get; init; }
	public decimal OrderPrice { get; init; }
	public decimal TradePrice { get; init; }
	public DateTime ServerTime { get; init; }
}
