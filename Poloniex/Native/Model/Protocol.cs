namespace StockSharp.Poloniex.Native.Model;

sealed class PoloniexMarket
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("baseCurrencyName")]
	public string BaseCurrency { get; set; }

	[JsonProperty("quoteCurrencyName")]
	public string QuoteCurrency { get; set; }

	[JsonProperty("displayName")]
	public string DisplayName { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("symbolTradeLimit")]
	public PoloniexTradeLimit TradeLimit { get; set; }
}

sealed class PoloniexTradeLimit
{
	[JsonProperty("priceScale")]
	public int PriceScale { get; set; }

	[JsonProperty("quantityScale")]
	public int QuantityScale { get; set; }

	[JsonProperty("minQuantity")]
	public decimal MinQuantity { get; set; }

	[JsonProperty("minAmount")]
	public decimal MinAmount { get; set; }
}

sealed class PoloniexCurrency
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("withdrawalFee")]
	public decimal? WithdrawalFee { get; set; }

	[JsonProperty("minConf")]
	public int MinConfirmations { get; set; }

	[JsonProperty("delisted")]
	public bool IsDelisted { get; set; }

	[JsonProperty("tradingState")]
	public string TradingState { get; set; }

	[JsonProperty("walletState")]
	public string WalletState { get; set; }
}

sealed class PoloniexTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; set; }

	[JsonProperty("amount")]
	public decimal? Amount { get; set; }

	[JsonProperty("dailyChange")]
	public decimal? DailyChange { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("bidQuantity")]
	public decimal? BidQuantity { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("askQuantity")]
	public decimal? AskQuantity { get; set; }

	[JsonProperty("closeTime")]
	public long CloseTime { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class PoloniexPublicTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("takerSide")]
	public string TakerSide { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
sealed class PoloniexCandle
{
	public decimal Low { get; set; }
	public decimal High { get; set; }
	public decimal Open { get; set; }
	public decimal Close { get; set; }
	public decimal Amount { get; set; }
	public decimal Quantity { get; set; }
	public decimal BuyTakerAmount { get; set; }
	public decimal BuyTakerQuantity { get; set; }
	public long TradeCount { get; set; }
	public long Timestamp { get; set; }
	public decimal WeightedAverage { get; set; }
	public string Interval { get; set; }
	public long StartTime { get; set; }
	public long CloseTime { get; set; }
}

sealed class PoloniexBookUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("asks")]
	public decimal[][] Asks { get; set; }

	[JsonProperty("bids")]
	public decimal[][] Bids { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("lastId")]
	public long LastId { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class PoloniexAccountBalances
{
	[JsonProperty("accountId")]
	public string AccountId { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("balances")]
	public PoloniexBalance[] Balances { get; set; }
}

sealed class PoloniexBalance
{
	[JsonProperty("currencyId")]
	public string CurrencyId { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("hold")]
	public decimal Hold { get; set; }
}

sealed class PoloniexBalanceUpdate
{
	[JsonProperty("changeTime")]
	public long ChangeTime { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("available")]
	public decimal Available { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("hold")]
	public decimal Hold { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class PoloniexOrder
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("filledAmount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("updateTime")]
	public long UpdateTime { get; set; }
}

sealed class PoloniexOrderUpdate
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("tradeFee")]
	public decimal TradeFee { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("filledQuantity")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("filledAmount")]
	public decimal FilledAmount { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }

	[JsonProperty("tradeTime")]
	public long TradeTime { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("tradeQty")]
	public decimal TradeQuantity { get; set; }

	[JsonProperty("tradePrice")]
	public decimal TradePrice { get; set; }

	[JsonProperty("tradeId")]
	public long TradeId { get; set; }

	[JsonProperty("ts")]
	public long Timestamp { get; set; }
}

sealed class PoloniexOwnTrade
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("orderId")]
	public long OrderId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("createTime")]
	public long CreateTime { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("feeCurrency")]
	public string FeeCurrency { get; set; }

	[JsonProperty("feeAmount")]
	public decimal FeeAmount { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class PoloniexOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("timeInForce", NullValueHandling = NullValueHandling.Ignore)]
	public string TimeInForce { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("quantity", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Quantity { get; set; }

	[JsonProperty("amount", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Amount { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class PoloniexReplaceOrderRequest
{
	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("quantity", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Quantity { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeInForce")]
	public string TimeInForce { get; set; }
}

sealed class PoloniexCancelAllRequest
{
	[JsonProperty("symbols", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Symbols { get; set; }
}

sealed class PoloniexWithdrawRequest
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }

	[JsonProperty("address")]
	public string Address { get; set; }

	[JsonProperty("paymentId", NullValueHandling = NullValueHandling.Ignore)]
	public string PaymentId { get; set; }
}

sealed class PoloniexOrderResult
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class PoloniexReplaceOrderResult
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("clientOrderId")]
	public string ClientOrderId { get; set; }
}

sealed class PoloniexWithdrawResult
{
	[JsonProperty("withdrawalRequestsId")]
	public long Id { get; set; }
}

sealed class PoloniexApiError
{
	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class PoloniexWsHeader
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class PoloniexWsEnvelope<T>
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("data")]
	public T[] Data { get; set; }
}

sealed class PoloniexWsAuthEnvelope
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("data")]
	public PoloniexWsAuthResult Data { get; set; }
}

sealed class PoloniexWsAuthResult
{
	[JsonProperty("success")]
	public bool Success { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class PoloniexWsCommand
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("channel", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Channel { get; set; }

	[JsonProperty("symbols", NullValueHandling = NullValueHandling.Ignore)]
	public string[] Symbols { get; set; }

	[JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)]
	public PoloniexWsAuthParameters Parameters { get; set; }
}

sealed class PoloniexWsAuthParameters
{
	[JsonProperty("key")]
	public string Key { get; set; }

	[JsonProperty("signTimestamp")]
	public long Timestamp { get; set; }

	[JsonProperty("signatureMethod")]
	public string SignatureMethod { get; set; }

	[JsonProperty("signatureVersion")]
	public string SignatureVersion { get; set; }

	[JsonProperty("signature")]
	public string Signature { get; set; }
}
