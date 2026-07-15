namespace StockSharp.Fyers.Native.Model;

class FyersResponse
{
	[JsonProperty("s")]
	public FyersResponseStatuses Status { get; set; }

	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class FyersProfileResponse : FyersResponse
{
	[JsonProperty("data")]
	public FyersProfile Data { get; set; }
}

sealed class FyersProfile
{
	[JsonProperty("fy_id")]
	public string ClientId { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("display_name")]
	public string DisplayName { get; set; }
}

sealed class FyersFundsResponse : FyersResponse
{
	[JsonProperty("fund_limit")]
	public FyersFund[] Funds { get; set; }
}

sealed class FyersFund
{
	[JsonProperty("id")]
	public int Id { get; set; }

	[JsonProperty("title")]
	public string Title { get; set; }

	[JsonProperty("equityAmount")]
	public decimal EquityAmount { get; set; }

	[JsonProperty("commodityAmount")]
	public decimal CommodityAmount { get; set; }
}

sealed class FyersPositionsResponse : FyersResponse
{
	[JsonProperty("netPositions")]
	public FyersPosition[] Positions { get; set; }
}

sealed class FyersPosition
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("buyAvg")]
	public decimal BuyAverage { get; set; }

	[JsonProperty("buyQty")]
	public decimal BuyQuantity { get; set; }

	[JsonProperty("sellAvg")]
	public decimal SellAverage { get; set; }

	[JsonProperty("sellQty")]
	public decimal SellQuantity { get; set; }

	[JsonProperty("netAvg")]
	public decimal NetAverage { get; set; }

	[JsonProperty("netQty")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("side")]
	public FyersSides Side { get; set; }

	[JsonProperty("productType")]
	public FyersProducts Product { get; set; }

	[JsonProperty("realized_profit")]
	public decimal RealizedProfit { get; set; }

	[JsonProperty("unrealized_profit")]
	public decimal UnrealizedProfit { get; set; }

	[JsonProperty("pl")]
	public decimal ProfitLoss { get; set; }

	[JsonProperty("fyToken")]
	public string Token { get; set; }

	[JsonProperty("exchange")]
	public FyersExchanges Exchange { get; set; }

	[JsonProperty("segment")]
	public FyersSegments Segment { get; set; }
}

sealed class FyersHoldingsResponse : FyersResponse
{
	[JsonProperty("holdings")]
	public FyersHolding[] Holdings { get; set; }
}

sealed class FyersHolding
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("fyToken")]
	public string Token { get; set; }

	[JsonProperty("isin")]
	public string Isin { get; set; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("qty_t1")]
	public decimal T1Quantity { get; set; }

	[JsonProperty("costPrice")]
	public decimal CostPrice { get; set; }

	[JsonProperty("ltp")]
	public decimal LastPrice { get; set; }

	[JsonProperty("pl")]
	public decimal ProfitLoss { get; set; }

	[JsonProperty("exchange")]
	public FyersExchanges Exchange { get; set; }

	[JsonProperty("segment")]
	public FyersSegments Segment { get; set; }
}

sealed class FyersOrdersResponse : FyersResponse
{
	[JsonProperty("orderBook")]
	public FyersOrder[] Orders { get; set; }
}

sealed class FyersOrder
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("exchOrdId")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("remainingQuantity")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("filledQty")]
	public decimal FilledQuantity { get; set; }

	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("tradedPrice")]
	public decimal TradedPrice { get; set; }

	[JsonProperty("type")]
	public FyersApiOrderTypes Type { get; set; }

	[JsonProperty("side")]
	public FyersSides Side { get; set; }

	[JsonProperty("status")]
	public FyersOrderStatuses OrderStatus { get; set; }

	[JsonProperty("productType")]
	public FyersProducts Product { get; set; }

	[JsonProperty("orderValidity")]
	public FyersValidityTypes Validity { get; set; }

	[JsonProperty("offlineOrder")]
	public bool IsAfterMarket { get; set; }

	[JsonProperty("orderDateTime")]
	public string OrderTime { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("orderTag")]
	public string OrderTag { get; set; }

	[JsonProperty("fyToken")]
	public string Token { get; set; }
}

sealed class FyersTradesResponse : FyersResponse
{
	[JsonProperty("tradeBook")]
	public FyersTrade[] Trades { get; set; }
}

sealed class FyersTrade
{
	[JsonProperty("tradeNumber")]
	public string TradeId { get; set; }

	[JsonProperty("orderNumber")]
	public string OrderId { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("tradedQty")]
	public decimal Quantity { get; set; }

	[JsonProperty("tradePrice")]
	public decimal Price { get; set; }

	[JsonProperty("side")]
	public FyersSides Side { get; set; }

	[JsonProperty("productType")]
	public FyersProducts Product { get; set; }

	[JsonProperty("orderType")]
	public FyersApiOrderTypes OrderType { get; set; }

	[JsonProperty("orderDateTime")]
	public string TradeTime { get; set; }

	[JsonProperty("fyToken")]
	public string Token { get; set; }
}

sealed class FyersOrderRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("qty")]
	public long Quantity { get; set; }

	[JsonProperty("type")]
	public FyersApiOrderTypes Type { get; set; }

	[JsonProperty("side")]
	public FyersSides Side { get; set; }

	[JsonProperty("productType")]
	public FyersProducts Product { get; set; }

	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("disclosedQty")]
	public long DisclosedQuantity { get; set; }

	[JsonProperty("validity")]
	public FyersValidityTypes Validity { get; set; }

	[JsonProperty("offlineOrder")]
	public bool IsAfterMarket { get; set; }

	[JsonProperty("isSliceOrder")]
	public bool IsSliceOrder { get; set; }

	[JsonProperty("stopLoss")]
	public decimal StopLoss { get; set; }

	[JsonProperty("takeProfit")]
	public decimal TakeProfit { get; set; }

	[JsonProperty("orderTag")]
	public string OrderTag { get; set; }
}

sealed class FyersModifyOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("type")]
	public FyersApiOrderTypes Type { get; set; }

	[JsonProperty("limitPrice")]
	public decimal LimitPrice { get; set; }

	[JsonProperty("stopPrice")]
	public decimal StopPrice { get; set; }

	[JsonProperty("qty")]
	public long Quantity { get; set; }
}

sealed class FyersCancelOrderRequest
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class FyersOrderResult : FyersResponse
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class FyersGttOrderRequest
{
	[JsonProperty("side")]
	public FyersSides Side { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("productType")]
	public FyersProducts Product { get; set; }

	[JsonProperty("orderInfo")]
	public FyersGttOrderInfo OrderInfo { get; set; }
}

sealed class FyersGttModifyRequest
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("orderInfo")]
	public FyersGttOrderInfo OrderInfo { get; set; }
}

sealed class FyersGttOrderInfo
{
	[JsonProperty("leg1")]
	public FyersGttLeg FirstLeg { get; set; }

	[JsonProperty("leg2")]
	public FyersGttLeg SecondLeg { get; set; }
}

sealed class FyersGttLeg
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("triggerPrice")]
	public decimal TriggerPrice { get; set; }

	[JsonProperty("qty")]
	public long Quantity { get; set; }
}

sealed class FyersHistoryResponse : FyersResponse
{
	[JsonProperty("candles")]
	public decimal[][] Candles { get; set; }
}

sealed class FyersTbtUrlResponse : FyersResponse
{
	[JsonProperty("data")]
	public FyersTbtUrl Data { get; set; }
}

sealed class FyersTbtUrl
{
	[JsonProperty("socket_url")]
	public string SocketUrl { get; set; }
}

sealed class FyersInstrument
{
	public string Token { get; set; }
	public string Name { get; set; }
	public int InstrumentType { get; set; }
	public decimal LotSize { get; set; }
	public decimal TickSize { get; set; }
	public string Isin { get; set; }
	public DateTime? ExpiryDate { get; set; }
	public string Symbol { get; set; }
	public FyersExchanges Exchange { get; set; }
	public FyersSegments Segment { get; set; }
	public string ShortName { get; set; }
	public string UnderlyingToken { get; set; }
	public decimal? Strike { get; set; }
	public string OptionType { get; set; }
}

sealed class FyersCandle
{
	public DateTime Time { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal Volume { get; set; }
}
