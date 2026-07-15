namespace StockSharp.Dhan.Native.Model;

sealed class DhanInstrument
{
	public string Exchange { get; set; }
	public string Segment { get; set; }
	public string SecurityId { get; set; }
	public string Isin { get; set; }
	public string Instrument { get; set; }
	public string UnderlyingSecurityId { get; set; }
	public string UnderlyingSymbol { get; set; }
	public string SymbolName { get; set; }
	public string DisplayName { get; set; }
	public string InstrumentType { get; set; }
	public string Series { get; set; }
	public decimal? LotSize { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public decimal? StrikePrice { get; set; }
	public string OptionType { get; set; }
	public decimal? TickSize { get; set; }
}

sealed class DhanNoRequest
{
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
abstract class DhanRequest
{
	[JsonProperty("dhanClientId")]
	public string ClientId { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrderRequest : DhanRequest
{
	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("disclosedQuantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("afterMarketOrder")]
	public bool AfterMarketOrder { get; set; }

	[JsonProperty("amoTime", NullValueHandling = NullValueHandling.Ignore)]
	public string AfterMarketTime { get; set; }

	[JsonProperty("boProfitValue", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? BracketProfit { get; set; }

	[JsonProperty("boStopLossValue", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? BracketStopLoss { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanModifyOrderRequest : DhanRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("legName")]
	public string LegName { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("disclosedQuantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanForeverOrderRequest : DhanRequest
{
	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("orderFlag")]
	public string OrderFlag { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("disclosedQuantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("price1", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? SecondPrice { get; set; }

	[JsonProperty("triggerPrice1", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? SecondTriggerPrice { get; set; }

	[JsonProperty("quantity1", NullValueHandling = NullValueHandling.Ignore)]
	public long? SecondQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanForeverModifyRequest : DhanRequest
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderFlag")]
	public string OrderFlag { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("legName")]
	public string LegName { get; set; }

	[JsonProperty("quantity")]
	public long Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("disclosedQuantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanOrder
{
	[JsonProperty("dhanClientId")]
	public string ClientId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("exchangeOrderId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosedQuantity")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("afterMarketOrder")]
	public bool AfterMarketOrder { get; set; }

	[JsonProperty("boProfitValue")]
	public decimal BracketProfit { get; set; }

	[JsonProperty("boStopLossValue")]
	public decimal BracketStopLoss { get; set; }

	[JsonProperty("legName")]
	public string LegName { get; set; }

	[JsonProperty("createTime")]
	public string CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }

	[JsonProperty("exchangeTime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("omsErrorCode")]
	public string ErrorCode { get; set; }

	[JsonProperty("omsErrorDescription")]
	public string ErrorDescription { get; set; }

	[JsonProperty("remainingQuantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("averageTradedPrice")]
	public decimal AverageTradedPrice { get; set; }

	[JsonProperty("filledQty")]
	public decimal FilledQuantity { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanForeverOrder
{
	[JsonProperty("correlationId")]
	public string CorrelationId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("orderType")]
	public string OrderFlag { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("legName")]
	public string LegName { get; set; }

	[JsonProperty("createTime")]
	public string CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public string UpdateTime { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanTrade
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("exchangeOrderId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("exchangeTradeId")]
	public string ExchangeTradeId { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("tradedQuantity")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("tradedPrice")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("exchangeTime")]
	public string ExchangeTime { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanPosition
{
	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("netQty")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("costPrice")]
	public decimal CostPrice { get; set; }

	[JsonProperty("realizedProfit")]
	public decimal RealizedProfit { get; set; }

	[JsonProperty("unrealizedProfit")]
	public decimal UnrealizedProfit { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanHolding
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("totalQty")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("availableQty")]
	public decimal AvailableQuantity { get; set; }

	[JsonProperty("avgCostPrice")]
	public decimal AverageCostPrice { get; set; }

	[JsonProperty("lastTradedPrice")]
	public decimal? LastTradedPrice { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanFunds
{
	[JsonProperty("availabelBalance")]
	public decimal AvailableBalance { get; set; }

	[JsonProperty("sodLimit")]
	public decimal StartOfDayLimit { get; set; }

	[JsonProperty("collateralAmount")]
	public decimal CollateralAmount { get; set; }

	[JsonProperty("receiveableAmount")]
	public decimal ReceivableAmount { get; set; }

	[JsonProperty("utilizedAmount")]
	public decimal UtilizedAmount { get; set; }

	[JsonProperty("blockedPayoutAmount")]
	public decimal BlockedPayoutAmount { get; set; }

	[JsonProperty("withdrawableBalance")]
	public decimal WithdrawableBalance { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanHistoryRequest : DhanRequest
{
	[JsonProperty("securityId")]
	public string SecurityId { get; set; }

	[JsonProperty("exchangeSegment")]
	public string ExchangeSegment { get; set; }

	[JsonProperty("instrument")]
	public string Instrument { get; set; }

	[JsonProperty("interval", NullValueHandling = NullValueHandling.Ignore)]
	public int? Interval { get; set; }

	[JsonProperty("expiryCode", NullValueHandling = NullValueHandling.Ignore)]
	public int? ExpiryCode { get; set; }

	[JsonProperty("oi")]
	public bool IncludeOpenInterest { get; set; }

	[JsonProperty("fromDate")]
	public string From { get; set; }

	[JsonProperty("toDate")]
	public string To { get; set; }
}

[Obfuscation(Feature = "renaming", ApplyToMembers = true)]
sealed class DhanCandleData
{
	[JsonProperty("open")]
	public decimal[] Open { get; set; }

	[JsonProperty("high")]
	public decimal[] High { get; set; }

	[JsonProperty("low")]
	public decimal[] Low { get; set; }

	[JsonProperty("close")]
	public decimal[] Close { get; set; }

	[JsonProperty("volume")]
	public decimal[] Volume { get; set; }

	[JsonProperty("timestamp")]
	public long[] Timestamp { get; set; }

	[JsonProperty("open_interest")]
	public decimal[] OpenInterest { get; set; }
}

sealed class DhanCandle
{
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
	public decimal? OpenInterest { get; set; }
}
