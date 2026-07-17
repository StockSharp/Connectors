namespace StockSharp.MotilalOswal.Native.Model;

internal sealed class MotilalOswalResponse<T>
	where T : class
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("data")]
	public T Data { get; set; }
}

internal sealed class MotilalOswalClientRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }
}

internal sealed class MotilalOswalExchangeRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("exchangename")]
	public string ExchangeName { get; set; }
}

internal sealed class MotilalOswalProfile
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("exchanges")]
	public string[] Exchanges { get; set; }

	[JsonProperty("products")]
	public string[] Products { get; set; }

	[JsonProperty("usertype")]
	public string UserType { get; set; }
}

internal sealed class MotilalOswalBroadcastLimit
{
	[JsonProperty("MaxBroadcastLimit")]
	public int Maximum { get; set; }
}

internal sealed class MotilalOswalInstrument
{
	[JsonProperty("exchange")]
	public int ExchangeId { get; set; }

	[JsonProperty("exchangename")]
	public string ExchangeName { get; set; }

	[JsonProperty("scripcode")]
	public long ScripCode { get; set; }

	[JsonProperty("scripname")]
	public string Name { get; set; }

	[JsonProperty("marketlot")]
	public decimal MarketLot { get; set; }

	[JsonProperty("scripshortname")]
	public string ShortName { get; set; }

	[JsonProperty("issuspended")]
	public string IsSuspendedValue { get; set; }

	[JsonProperty("instrumentname")]
	public string InstrumentName { get; set; }

	[JsonProperty("expirydate")]
	public long ExpirySeconds { get; set; }

	[JsonProperty("strikeprice")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("optiontype")]
	public string OptionType { get; set; }

	[JsonProperty("markettype")]
	public string MarketType { get; set; }

	[JsonProperty("foexposurepercent")]
	public decimal ExposurePercent { get; set; }

	[JsonProperty("lowercircuitprice")]
	public decimal LowerCircuitPrice { get; set; }

	[JsonProperty("uppercircuitprice")]
	public decimal UpperCircuitPrice { get; set; }

	[JsonProperty("ticksize")]
	public decimal TickSize { get; set; }

	[JsonProperty("scripisinno")]
	public string Isin { get; set; }

	[JsonProperty("indicesidentifier")]
	public int IndexIdentifier { get; set; }

	[JsonProperty("isbanscrip")]
	public string IsBannedValue { get; set; }

	[JsonProperty("scripfullname")]
	public string FullName { get; set; }

	[JsonProperty("facevalue")]
	public decimal FaceValue { get; set; }

	[JsonProperty("calevel")]
	public decimal CallAuctionLevel { get; set; }

	[JsonProperty("maxqtyperorder")]
	public decimal MaximumOrderQuantity { get; set; }

	[JsonIgnore]
	public bool IsIndex { get; set; }
}

internal sealed class MotilalOswalIndex
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("exchangename")]
	public string ExchangeName { get; set; }

	[JsonProperty("indexcode")]
	public long IndexCode { get; set; }

	[JsonProperty("indexname")]
	public string IndexName { get; set; }
}

internal sealed class MotilalOswalPlaceOrderRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public long SymbolToken { get; set; }

	[JsonProperty("buyorsell")]
	public string Side { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("orderduration")]
	public string Duration { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerprice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("quantityinlot")]
	public long Quantity { get; set; }

	[JsonProperty("disclosedquantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("amoorder")]
	public string AfterMarket { get; set; }

	[JsonProperty("goodtilldate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("algoid")]
	public string AlgoId { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("participantcode")]
	public string ParticipantCode { get; set; }
}

internal sealed class MotilalOswalPlaceOrderResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }
}

internal sealed class MotilalOswalStatusResponse
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }
}

internal sealed class MotilalOswalModifyOrderRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }

	[JsonProperty("newordertype")]
	public string OrderType { get; set; }

	[JsonProperty("neworderduration")]
	public string Duration { get; set; }

	[JsonProperty("newprice")]
	public decimal Price { get; set; }

	[JsonProperty("newtriggerprice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("newquantityinlot")]
	public long Quantity { get; set; }

	[JsonProperty("newdisclosedquantity")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("newgoodtilldate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("lastmodifiedtime")]
	public string LastModifiedTime { get; set; }

	[JsonProperty("qtytradedtoday")]
	public long TradedQuantity { get; set; }
}

internal sealed class MotilalOswalCancelOrderRequest
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }
}

internal class MotilalOswalOrder
{
	[JsonProperty("ordercategory")]
	public string OrderCategory { get; set; }

	[JsonProperty("clientid")]
	public string ClientId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public long SymbolToken { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("series")]
	public string Series { get; set; }

	[JsonProperty("expirydate")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strikeprice")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("optiontype")]
	public string OptionType { get; set; }

	[JsonProperty("orderid")]
	public string OrderId { get; set; }

	[JsonProperty("orderinitiatorid")]
	public string OrderInitiatorId { get; set; }

	[JsonProperty("ordertype")]
	public string OrderType { get; set; }

	[JsonProperty("booktype")]
	public string BookType { get; set; }

	[JsonProperty("orderduration")]
	public string Duration { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("orderstatus")]
	public string OrderStatus { get; set; }

	[JsonProperty("buyorsell")]
	public string Side { get; set; }

	[JsonProperty("totalqtyremaining")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("orderqty")]
	public decimal OrderQuantity { get; set; }

	[JsonProperty("qtytradedtoday")]
	public decimal TradedQuantityToday { get; set; }

	[JsonProperty("disclosedqty")]
	public decimal DisclosedQuantity { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerprice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("entrydatetime")]
	public string EntryTime { get; set; }

	[JsonProperty("lastmodifiedtime")]
	public string LastModifiedTime { get; set; }

	[JsonProperty("vendor")]
	public string Vendor { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }

	[JsonProperty("goodtilldate")]
	public string GoodTillDate { get; set; }

	[JsonProperty("amoorder")]
	public string AfterMarket { get; set; }

	[JsonProperty("algoid")]
	public string AlgoId { get; set; }

	[JsonProperty("algocategory")]
	public int AlgoCategory { get; set; }

	[JsonProperty("averageprice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("totalqtytraded")]
	public decimal TotalTradedQuantity { get; set; }

	[JsonProperty("lotsize")]
	public decimal LotSize { get; set; }

	[JsonProperty("tag")]
	public string Tag { get; set; }

	[JsonProperty("recordinserttime")]
	public string RecordInsertTime { get; set; }

	[JsonProperty("participantcode")]
	public string ParticipantCode { get; set; }
}

internal class MotilalOswalTrade
{
	[JsonProperty("clientid")]
	public string ClientId { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("symboltoken")]
	public long SymbolToken { get; set; }

	[JsonProperty("producttype")]
	public string ProductType { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("instrumenttype")]
	public string InstrumentType { get; set; }

	[JsonProperty("series")]
	public string Series { get; set; }

	[JsonProperty("strikeprice")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("optiontype")]
	public string OptionType { get; set; }

	[JsonProperty("expirydate")]
	public string ExpiryDate { get; set; }

	[JsonProperty("lotsize")]
	public decimal LotSize { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }

	[JsonProperty("multiplier")]
	public decimal Multiplier { get; set; }

	[JsonProperty("tradeprice")]
	public decimal TradePrice { get; set; }

	[JsonProperty("tradeqty")]
	public decimal TradeQuantity { get; set; }

	[JsonProperty("tradevalue")]
	public decimal TradeValue { get; set; }

	[JsonProperty("buyorsell")]
	public string Side { get; set; }

	[JsonProperty("orderid")]
	public string OrderId { get; set; }

	[JsonProperty("tradeno")]
	public string TradeNumber { get; set; }

	[JsonProperty("tradetime")]
	public string TradeTime { get; set; }

	[JsonProperty("uniqueorderid")]
	public string UniqueOrderId { get; set; }
}

internal sealed class MotilalOswalHolding
{
	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("scripisinno")]
	public string Isin { get; set; }

	[JsonProperty("dpquantity")]
	public decimal DepositoryQuantity { get; set; }

	[JsonProperty("blockedquantity")]
	public decimal BlockedQuantity { get; set; }

	[JsonProperty("scripname")]
	public string Name { get; set; }

	[JsonProperty("buyavgprice")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("poaquantity")]
	public decimal PoaQuantity { get; set; }

	[JsonProperty("collateralquantity")]
	public decimal CollateralQuantity { get; set; }

	[JsonProperty("outstandingquantity")]
	public decimal OutstandingQuantity { get; set; }

	[JsonProperty("debitstockquantity")]
	public decimal DebitStockQuantity { get; set; }

	[JsonProperty("nonpoaquantity")]
	public decimal NonPoaQuantity { get; set; }

	[JsonProperty("rmssellingquantity")]
	public decimal RmsSellingQuantity { get; set; }

	[JsonProperty("btstquantity")]
	public decimal BtstQuantity { get; set; }

	[JsonProperty("buybackquantity")]
	public decimal BuybackQuantity { get; set; }

	[JsonProperty("tpinquantity")]
	public decimal TpinQuantity { get; set; }

	[JsonProperty("slbmquantity")]
	public decimal SlbmQuantity { get; set; }

	[JsonProperty("nbfcquantity")]
	public decimal NbfcQuantity { get; set; }

	[JsonProperty("bsescripcode")]
	public long BseScripCode { get; set; }

	[JsonProperty("nsesymboltoken")]
	public long NseSymbolToken { get; set; }
}

internal sealed class MotilalOswalPosition
{
	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("clientcode")]
	public string ClientCode { get; set; }

	[JsonProperty("productname")]
	public string ProductName { get; set; }

	[JsonProperty("symboltoken")]
	public long SymbolToken { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("buyquantity")]
	public decimal BuyQuantity { get; set; }

	[JsonProperty("buyamount")]
	public decimal BuyAmount { get; set; }

	[JsonProperty("sellquantity")]
	public decimal SellQuantity { get; set; }

	[JsonProperty("sellamount")]
	public decimal SellAmount { get; set; }

	[JsonProperty("daybuyquantity")]
	public decimal DayBuyQuantity { get; set; }

	[JsonProperty("daybuyamount")]
	public decimal DayBuyAmount { get; set; }

	[JsonProperty("daysellquantity")]
	public decimal DaySellQuantity { get; set; }

	[JsonProperty("daysellamount")]
	public decimal DaySellAmount { get; set; }

	[JsonProperty("LTP")]
	public decimal LastPrice { get; set; }

	[JsonProperty("marktomarket")]
	public decimal MarkToMarket { get; set; }

	[JsonProperty("bookedprofitloss")]
	public decimal BookedProfitLoss { get; set; }

	[JsonProperty("cfbuyquantity")]
	public decimal CarryForwardBuyQuantity { get; set; }

	[JsonProperty("cfbuyamount")]
	public decimal CarryForwardBuyAmount { get; set; }

	[JsonProperty("cfsellquantity")]
	public decimal CarryForwardSellQuantity { get; set; }

	[JsonProperty("cfsellamount")]
	public decimal CarryForwardSellAmount { get; set; }

	[JsonProperty("actualbookedprofitloss")]
	public decimal ActualBookedProfitLoss { get; set; }

	[JsonProperty("actualmarktomarket")]
	public decimal ActualMarkToMarket { get; set; }

	[JsonProperty("series")]
	public string Series { get; set; }

	[JsonProperty("expirydate")]
	public string ExpiryDate { get; set; }

	[JsonProperty("strikeprice")]
	public decimal StrikePrice { get; set; }

	[JsonProperty("optiontype")]
	public string OptionType { get; set; }
}

internal sealed class MotilalOswalMarginRow
{
	[JsonProperty("srno")]
	public int SerialNumber { get; set; }

	[JsonProperty("particulars")]
	public string Particulars { get; set; }

	[JsonProperty("amount")]
	public decimal Amount { get; set; }
}

internal sealed class MotilalOswalTradeSocketLogin
{
	[JsonProperty("clientid")]
	public string ClientId { get; set; }

	[JsonProperty("authtoken")]
	public string AuthToken { get; set; }

	[JsonProperty("apikey")]
	public string ApiKey { get; set; }
}

internal sealed class MotilalOswalTradeSocketAction
{
	[JsonProperty("clientid")]
	public string ClientId { get; set; }

	[JsonProperty("action")]
	public string Action { get; set; }
}

internal sealed class MotilalOswalTradeStreamEvent : MotilalOswalOrder
{
	[JsonProperty("status")]
	public string Status { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("errorcode")]
	public string ErrorCode { get; set; }

	[JsonProperty("instrumenttype")]
	public string InstrumentType { get; set; }

	[JsonProperty("precision")]
	public int Precision { get; set; }

	[JsonProperty("multiplier")]
	public decimal Multiplier { get; set; }

	[JsonProperty("tradeprice")]
	public decimal TradePrice { get; set; }

	[JsonProperty("tradeqty")]
	public decimal TradeQuantity { get; set; }

	[JsonProperty("tradevalue")]
	public decimal TradeValue { get; set; }

	[JsonProperty("tradeno")]
	public string TradeNumber { get; set; }

	[JsonProperty("tradetime")]
	public string TradeTime { get; set; }
}

internal enum MotilalOswalMarketMessageTypes
{
	LastTrade,
	Depth,
	DayOhlc,
	CircuitLimits,
	OpenInterest,
	Index,
}

internal sealed class MotilalOswalMarketUpdate
{
	public MotilalOswalMarketMessageTypes MessageType { get; set; }
	public string Exchange { get; set; }
	public long ScripCode { get; set; }
	public DateTime ServerTime { get; set; }
	public decimal LastPrice { get; set; }
	public decimal LastQuantity { get; set; }
	public decimal CumulativeQuantity { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal OpenInterest { get; set; }
	public decimal OpenInterestHigh { get; set; }
	public decimal OpenInterestLow { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal HighPrice { get; set; }
	public decimal LowPrice { get; set; }
	public decimal PreviousClose { get; set; }
	public decimal UpperCircuit { get; set; }
	public decimal LowerCircuit { get; set; }
	public int DepthLevel { get; set; }
	public decimal BidPrice { get; set; }
	public decimal BidQuantity { get; set; }
	public int BidOrders { get; set; }
	public decimal AskPrice { get; set; }
	public decimal AskQuantity { get; set; }
	public int AskOrders { get; set; }
}

internal sealed class MotilalOswalDepthLevel
{
	public decimal BidPrice { get; set; }
	public decimal BidQuantity { get; set; }
	public int BidOrders { get; set; }
	public decimal AskPrice { get; set; }
	public decimal AskQuantity { get; set; }
	public int AskOrders { get; set; }
}
