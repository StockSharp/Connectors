namespace StockSharp.MiraeSharekhan.Native;

internal class MiraeSharekhanResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("success")]
	public string Success { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("error_code")]
	public string ErrorCode2 { get; set; }

	[JsonProperty("error_type")]
	public string ErrorType { get; set; }

	public string GetErrorCode() => ErrorCode.IsEmpty(ErrorCode2);

	public bool IsFailed()
	{
		if (!GetErrorCode().IsEmpty() || !ErrorType.IsEmpty())
			return true;
		var status = Status.IsEmpty(Success);
		return status.EqualsIgnoreCase("false") || status.EqualsIgnoreCase("failed") ||
			status.EqualsIgnoreCase("error");
	}
}

internal sealed class MiraeSharekhanItemsResponse<T> : MiraeSharekhanResponse
{
	[JsonProperty("data")]
	public T[] Data { get; set; }

	[JsonProperty("records")]
	public T[] Records { get; set; }

	[JsonProperty("result")]
	public T[] Result { get; set; }

	[JsonProperty("master")]
	public T[] Master { get; set; }

	[JsonProperty("orders")]
	public T[] Orders { get; set; }

	[JsonProperty("trades")]
	public T[] Trades { get; set; }

	[JsonProperty("holdings")]
	public T[] Holdings { get; set; }

	[JsonProperty("positions")]
	public T[] Positions { get; set; }

	[JsonProperty("candles")]
	public T[] Candles { get; set; }

	public T[] GetItems()
		=> Data ?? Records ?? Result ?? Master ?? Orders ?? Trades ?? Holdings ?? Positions ?? Candles ?? [];
}

internal sealed class MiraeSharekhanObjectResponse<T> : MiraeSharekhanResponse
	where T : class
{
	[JsonProperty("data")]
	public T Data { get; set; }

	[JsonProperty("result")]
	public T Result { get; set; }

	[JsonProperty("funds")]
	public T Funds { get; set; }

	public T GetValue() => Data ?? Result ?? Funds;
}

internal sealed class MiraeSharekhanInstrument
{
	[JsonProperty("scripCode")]
	public string ScripCode { get; set; }

	[JsonProperty("scripcode")]
	public string ScripCode2 { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("scripName")]
	public string ScripName { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("companyName")]
	public string CompanyName { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("segment")]
	public string Segment { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("tickSize")]
	public decimal? TickSize { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("expiryDate")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	public long GetScripCode()
		=> long.TryParse(ScripCode.IsEmpty(ScripCode2), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value) ? value : 0;

	public string GetSymbol()
		=> TradingSymbol.IsEmpty(Symbol).IsEmpty(ScripName).IsEmpty(GetScripCode().ToString(CultureInfo.InvariantCulture));

	public string GetName()
		=> CompanyName.IsEmpty(Name).IsEmpty(ScripName).IsEmpty(GetSymbol());

	public DateTime? GetExpiryDate() => ExpiryDate.IsEmpty(Expiry).ParseIndiaTime();
}

internal sealed class MiraeSharekhanHistoricalCandle
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("open")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal LowPrice { get; set; }

	[JsonProperty("close")]
	public decimal ClosePrice { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("oi")]
	public decimal? OpenInterest2 { get; set; }

	public DateTime? GetTime()
	{
		var value = Timestamp.IsEmpty(Date.IsEmpty() ? Time : $"{Date} {Time}".Trim());
		return value.ParseIndiaTime();
	}
}

internal sealed class MiraeSharekhanOrderRequest
{
	[JsonProperty("orderId", NullValueHandling = NullValueHandling.Ignore)]
	public string OrderId { get; set; }

	[JsonProperty("customerId")]
	public string CustomerId { get; set; }

	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosedQty")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("rmsCode")]
	public string RmsCode { get; set; }

	[JsonProperty("afterHour")]
	public string AfterHour { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; } = "NORMAL";

	[JsonProperty("channelUser")]
	public string ChannelUser { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; } = "GFD";

	[JsonProperty("requestType")]
	public string RequestType { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("strikePrice")]
	public decimal StrikePrice { get; set; } = -1;

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; } = "XX";
}

internal sealed class MiraeSharekhanOrderResponse : MiraeSharekhanResponse
{
	[JsonProperty("data")]
	public MiraeSharekhanOrderResult Data { get; set; }

	[JsonProperty("result")]
	public MiraeSharekhanOrderResult Result { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderNumber")]
	public string OrderNumber { get; set; }

	public string GetOrderId()
		=> Data?.GetOrderId().IsEmpty(Result?.GetOrderId()).IsEmpty(OrderId).IsEmpty(OrderNumber);
}

internal sealed class MiraeSharekhanOrderResult
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderNumber")]
	public string OrderNumber { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	public string GetOrderId() => OrderId.IsEmpty(OrderNumber);
}

internal class MiraeSharekhanOrder
{
	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("customerId")]
	public string CustomerId { get; set; }

	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("disclosedQty")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("executedQty")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("filledQty")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("pendingQty")]
	public decimal? PendingQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("orderStatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("orderTime")]
	public string OrderTime { get; set; }

	[JsonProperty("exchangeTime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("productType")]
	public string ProductType { get; set; }

	[JsonProperty("instrumentType")]
	public string InstrumentType { get; set; }

	[JsonProperty("strikePrice")]
	public decimal? StrikePrice { get; set; }

	[JsonProperty("optionType")]
	public string OptionType { get; set; }

	[JsonProperty("expiry")]
	public string Expiry { get; set; }

	[JsonProperty("rejectionReason")]
	public string RejectionReason { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	public decimal GetFilledQuantity() => Math.Max(ExecutedQuantity, FilledQuantity);
	public string GetStatus() => OrderStatus.IsEmpty(Status);
	public DateTime? GetTime() => ExchangeTime.IsEmpty(OrderTime).ParseIndiaTime();
}

internal sealed class MiraeSharekhanTrade
{
	[JsonProperty("tradeId")]
	public string TradeId { get; set; }

	[JsonProperty("exchangeTradeId")]
	public string ExchangeTradeId { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("transactionType")]
	public string TransactionType { get; set; }

	[JsonProperty("tradedQty")]
	public decimal TradedQuantity { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("tradedPrice")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tradeTime")]
	public string TradeTime { get; set; }

	[JsonProperty("exchangeTime")]
	public string ExchangeTime { get; set; }

	[JsonProperty("netQty")]
	public decimal? NetQuantity { get; set; }

	[JsonProperty("buyQty")]
	public decimal? BuyQuantity { get; set; }

	[JsonProperty("sellQty")]
	public decimal? SellQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("realizedPnl")]
	public decimal? RealizedPnL { get; set; }

	[JsonProperty("unrealizedPnl")]
	public decimal? UnrealizedPnL { get; set; }

	public string GetTradeId() => ExchangeTradeId.IsEmpty(TradeId);
	public decimal GetQuantity() => TradedQuantity > 0 ? TradedQuantity : Quantity;
	public decimal GetPrice() => TradedPrice > 0 ? TradedPrice : Price;
	public DateTime? GetTime() => ExchangeTime.IsEmpty(TradeTime).ParseIndiaTime();
}

internal sealed class MiraeSharekhanHolding
{
	[JsonProperty("scripCode")]
	public long ScripCode { get; set; }

	[JsonProperty("tradingSymbol")]
	public string TradingSymbol { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("totalQty")]
	public decimal TotalQuantity { get; set; }

	[JsonProperty("availableQty")]
	public decimal AvailableQuantity { get; set; }

	[JsonProperty("averagePrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("ltp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	public decimal GetQuantity()
		=> TotalQuantity != 0 ? TotalQuantity : Quantity != 0 ? Quantity : AvailableQuantity;
}

internal sealed class MiraeSharekhanFunds
{
	[JsonProperty("availableBalance")]
	public decimal? AvailableBalance { get; set; }

	[JsonProperty("availableMargin")]
	public decimal? AvailableMargin { get; set; }

	[JsonProperty("cashBalance")]
	public decimal? CashBalance { get; set; }

	[JsonProperty("openingBalance")]
	public decimal? OpeningBalance { get; set; }

	[JsonProperty("utilizedAmount")]
	public decimal? UtilizedAmount { get; set; }

	[JsonProperty("usedMargin")]
	public decimal? UsedMargin { get; set; }

	[JsonProperty("collateral")]
	public decimal? Collateral { get; set; }

	public decimal? GetAvailable() => AvailableBalance ?? AvailableMargin ?? CashBalance;
	public decimal? GetBlocked() => UtilizedAmount ?? UsedMargin;
}

internal sealed class MiraeSharekhanSocketRequest
{
	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("key")]
	public string[] Key { get; set; }

	[JsonProperty("value")]
	public string[] Value { get; set; }
}

internal class MiraeSharekhanStreamMessage
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("scripCode")]
	public string ScripCode { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ltp")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice2 { get; set; }

	[JsonProperty("lastQty")]
	public decimal? LastQuantity { get; set; }

	[JsonProperty("lastTradeQty")]
	public decimal? LastQuantity2 { get; set; }

	[JsonProperty("open")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("close")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("oi")]
	public decimal? OpenInterest { get; set; }

	[JsonProperty("openInterest")]
	public decimal? OpenInterest2 { get; set; }

	[JsonProperty("timestamp")]
	public string Timestamp { get; set; }

	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("bid")]
	public decimal? BestBidPrice { get; set; }

	[JsonProperty("bidQty")]
	public decimal? BestBidQuantity { get; set; }

	[JsonProperty("ask")]
	public decimal? BestAskPrice { get; set; }

	[JsonProperty("askQty")]
	public decimal? BestAskQuantity { get; set; }

	[JsonProperty("bids")]
	public MiraeSharekhanDepthLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public MiraeSharekhanDepthLevel[] Asks { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	public decimal? GetLastPrice() => LastPrice ?? LastPrice2;
	public decimal? GetLastQuantity() => LastQuantity ?? LastQuantity2;
	public decimal? GetOpenInterest() => OpenInterest ?? OpenInterest2;
	public DateTime? GetTime() => Timestamp.IsEmpty(Time).ParseIndiaTime();

	public string GetStreamKey()
	{
		if (!Token.IsEmpty())
			return Token.ToUpperInvariant();
		if (!Exchange.IsEmpty() && !ScripCode.IsEmpty())
			return Exchange.ToUpperInvariant() + ScripCode;
		return null;
	}
}

internal sealed class MiraeSharekhanStreamEnvelope : MiraeSharekhanStreamMessage
{
	[JsonProperty("data")]
	public MiraeSharekhanStreamMessage[] Data { get; set; }

	[JsonProperty("feeds")]
	public MiraeSharekhanStreamMessage[] Feeds { get; set; }

	public MiraeSharekhanStreamMessage[] GetMessages() => Data ?? Feeds ?? [];
}

internal sealed class MiraeSharekhanDepthLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity2 { get; set; }

	[JsonProperty("orders")]
	public int? Orders { get; set; }

	[JsonProperty("orderCount")]
	public int? OrderCount { get; set; }

	public decimal GetQuantity() => Quantity != 0 ? Quantity : Quantity2;
	public int? GetOrders() => Orders ?? OrderCount;
}
