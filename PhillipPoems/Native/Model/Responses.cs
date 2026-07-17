namespace StockSharp.PhillipPoems.Native.Model;

[DataContract]
class PoemsResponse
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

[DataContract]
sealed class PoemsTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("refresh_token")]
	public string RefreshToken { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public int ExpiresIn { get; set; }

	[JsonProperty("scope")]
	public string Scope { get; set; }

	[JsonProperty("accountNo")]
	public string AccountNo { get; set; }

	[JsonProperty("accountType")]
	public string AccountType { get; set; }

	[JsonProperty("sessionId")]
	public string SessionId { get; set; }

	[JsonProperty("deviceId")]
	public string DeviceId { get; set; }
}

[DataContract]
sealed class PoemsMarketsResponse : PoemsResponse
{
	[JsonProperty("markets")]
	public PoemsMarket[] Markets { get; set; }
}

[DataContract]
sealed class PoemsMarket
{
	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeDisplay")]
	public string ExchangeDisplay { get; set; }
}

[DataContract]
sealed class PoemsCounterIdResponse : PoemsResponse
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }
}

[DataContract]
sealed class PoemsCounterSearchResponse : PoemsResponse
{
	[JsonProperty("counterList")]
	public PoemsCounter[] CounterList { get; set; }
}

[DataContract]
sealed class PoemsCounter
{
	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("nameDisplay")]
	public string NameDisplay { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolDisplay")]
	public string SymbolDisplay { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangeDisplay")]
	public string ExchangeDisplay { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("productIcon")]
	public string ProductIcon { get; set; }

	[JsonProperty("pmpTopic")]
	public string PmpTopic { get; set; }

	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("delayIndicator")]
	public string DelayIndicator { get; set; }
}

[DataContract]
sealed class PoemsPriceListResponse : PoemsResponse
{
	[JsonProperty("priceList")]
	public PoemsPrice[] PriceList { get; set; }
}

[DataContract]
class PoemsPrice
{
	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("productIcon")]
	public string ProductIcon { get; set; }

	[JsonProperty("counterName")]
	public string CounterName { get; set; }

	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("lastDone")]
	public string LastDone { get; set; }

	[JsonProperty("change")]
	public string Change { get; set; }

	[JsonProperty("changePercent")]
	public string ChangePercent { get; set; }

	[JsonProperty("bid")]
	public string Bid { get; set; }

	[JsonProperty("ask")]
	public string Ask { get; set; }

	[JsonProperty("bVolK")]
	public string BidVolumeK { get; set; }

	[JsonProperty("sVolK")]
	public string AskVolumeK { get; set; }

	[JsonProperty("volK")]
	public string VolumeK { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("prevClose")]
	public string PreviousClose { get; set; }

	[JsonProperty("lastDoneDate")]
	public string LastDoneDate { get; set; }

	[JsonProperty("pmpTopic")]
	public string PmpTopic { get; set; }
}

[DataContract]
sealed class PoemsCounterInfoResponse : PoemsResponse
{
	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("productIcon")]
	public string ProductIcon { get; set; }

	[JsonProperty("counterName")]
	public string CounterName { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("lastDone")]
	public string LastDone { get; set; }

	[JsonProperty("change")]
	public string Change { get; set; }

	[JsonProperty("changePercent")]
	public string ChangePercent { get; set; }

	[JsonProperty("bid")]
	public string Bid { get; set; }

	[JsonProperty("ask")]
	public string Ask { get; set; }

	[JsonProperty("bVolK")]
	public string BidVolumeK { get; set; }

	[JsonProperty("sVolK")]
	public string AskVolumeK { get; set; }

	[JsonProperty("volK")]
	public string VolumeK { get; set; }

	[JsonProperty("open")]
	public string Open { get; set; }

	[JsonProperty("high")]
	public string High { get; set; }

	[JsonProperty("low")]
	public string Low { get; set; }

	[JsonProperty("prevClose")]
	public string PreviousClose { get; set; }

	[JsonProperty("lastDoneDate")]
	public string LastDoneDate { get; set; }
}

[DataContract]
sealed class PoemsTimeSalesResponse : PoemsResponse
{
	[JsonProperty("totalPage")]
	public int TotalPages { get; set; }

	[JsonProperty("timeSales")]
	public PoemsTimeSale[] TimeSales { get; set; }
}

[DataContract]
sealed class PoemsTimeSale
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("price")]
	public string Price { get; set; }

	[JsonProperty("vol")]
	public string Volume { get; set; }
}

[DataContract]
sealed class PoemsMarketDepthResponse : PoemsResponse
{
	[JsonProperty("marketDepth")]
	public PoemsDepthEntry[] MarketDepth { get; set; }
}

[DataContract]
sealed class PoemsDepthEntry
{
	[JsonProperty("bidPrice")]
	public string BidPrice { get; set; }

	[JsonProperty("bidVolume")]
	public string BidVolume { get; set; }

	[JsonProperty("bidVol")]
	public string BidVolumeShort { get; set; }

	[JsonProperty("buyPrice")]
	public string BuyPrice { get; set; }

	[JsonProperty("buyVolume")]
	public string BuyVolume { get; set; }

	[JsonProperty("askPrice")]
	public string AskPrice { get; set; }

	[JsonProperty("askVolume")]
	public string AskVolume { get; set; }

	[JsonProperty("askVol")]
	public string AskVolumeShort { get; set; }

	[JsonProperty("sellPrice")]
	public string SellPrice { get; set; }

	[JsonProperty("sellVolume")]
	public string SellVolume { get; set; }

	[JsonProperty("bidOrders")]
	public int? BidOrders { get; set; }

	[JsonProperty("askOrders")]
	public int? AskOrders { get; set; }
}

[DataContract]
sealed class PoemsOrdersResponse : PoemsResponse
{
	[JsonProperty("orders")]
	public PoemsOrder[] Orders { get; set; }
}

[DataContract]
sealed class PoemsOrder
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }

	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("submittedPrice")]
	public string SubmittedPrice { get; set; }

	[JsonProperty("submittedQty")]
	public string SubmittedQuantity { get; set; }

	[JsonProperty("remainingQty")]
	public string RemainingQuantity { get; set; }

	[JsonProperty("submittedTime")]
	public string SubmittedTime { get; set; }

	[JsonProperty("executedPrice")]
	public string ExecutedPrice { get; set; }

	[JsonProperty("executedQty")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("updatedTime")]
	public string UpdatedTime { get; set; }

	[JsonProperty("executedTime")]
	public string ExecutedTime { get; set; }

	[JsonProperty("orderNo")]
	public string OrderNo { get; set; }

	[JsonProperty("lotSize")]
	public string LotSize { get; set; }

	[JsonProperty("stopPrice")]
	public string StopPrice { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("orderStatusDesc")]
	public string OrderStatusDescription { get; set; }

	[JsonProperty("referenceNo")]
	public string ReferenceNo { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("paymentCurrency")]
	public string PaymentCurrency { get; set; }

	[JsonProperty("latestUpdatedTime")]
	public string LatestUpdatedTime { get; set; }
}

[DataContract]
sealed class PoemsOrderActionResponse : PoemsResponse
{
	[JsonProperty("passwordRequire")]
	public bool IsPasswordRequired { get; set; }

	[JsonProperty("twoFARequire")]
	public bool IsTwoFactorRequired { get; set; }

	[JsonProperty("authToken")]
	public string AuthToken { get; set; }

	[JsonProperty("orderNo")]
	public long? OrderNo { get; set; }

	[JsonProperty("orderDetailsURI")]
	public string OrderDetailsUri { get; set; }
}

[DataContract]
sealed class PoemsAccountDetailsResponse : PoemsResponse
{
	[JsonProperty("lastUpdated")]
	public string LastUpdated { get; set; }

	[JsonProperty("accountDetails")]
	public PoemsAccountDetail[] AccountDetails { get; set; }
}

[DataContract]
sealed class PoemsAccountDetail
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("openingBalanceToday")]
	public string OpeningBalance { get; set; }

	[JsonProperty("availableBalance")]
	public string AvailableBalance { get; set; }

	[JsonProperty("allItemsOutstanding")]
	public string OutstandingAmount { get; set; }

	[JsonProperty("totalAssetValue")]
	public string TotalAssetValue { get; set; }

	[JsonProperty("marginCall")]
	public string MarginCall { get; set; }

	[JsonProperty("creditLimit")]
	public string CreditLimit { get; set; }

	[JsonProperty("initialMargin")]
	public string InitialMargin { get; set; }
}

[DataContract]
sealed class PoemsHoldingsResponse : PoemsResponse
{
	[JsonProperty("lastUpdated")]
	public string LastUpdated { get; set; }

	[JsonProperty("holdings")]
	public PoemsExchangeHoldings[] Holdings { get; set; }
}

[DataContract]
sealed class PoemsExchangeHoldings
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("currencies")]
	public PoemsCurrencyHoldings[] Currencies { get; set; }
}

[DataContract]
sealed class PoemsCurrencyHoldings
{
	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("list")]
	public PoemsHolding[] Items { get; set; }
}

[DataContract]
sealed class PoemsHolding
{
	[JsonProperty("counterID")]
	public string CounterId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("unrealizedPL")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("mktValue")]
	public string MarketValue { get; set; }

	[JsonProperty("aveCostPrice")]
	public string AveragePrice { get; set; }

	[JsonProperty("closingPrice")]
	public string ClosingPrice { get; set; }

	[JsonProperty("tradedCurr")]
	public string Currency { get; set; }

	[JsonProperty("suspendedQty")]
	public string SuspendedQuantity { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("tradable")]
	public bool IsTradable { get; set; }
}
