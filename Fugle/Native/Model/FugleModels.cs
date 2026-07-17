namespace StockSharp.Fugle.Native.Model;

enum FugleAssetKinds
{
	Stock,
	FuturesOptions,
}

sealed class FugleSecurityInfo
{
	public FugleAssetKinds Kind { get; set; }
	public string TickerType { get; set; }
	public string Exchange { get; set; }
	public string Market { get; set; }
	public string Session { get; set; }
	public string Symbol { get; set; }
	public string Name { get; set; }
	public string ContractType { get; set; }
	public decimal? ReferencePrice { get; set; }
	public string StartDate { get; set; }
	public string EndDate { get; set; }
	public string SettlementDate { get; set; }
	public bool IsAfterHours => Session.EqualsIgnoreCase("AFTERHOURS");
}

sealed class FugleTickerListResponse
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("session")]
	public string Session { get; set; }

	[JsonProperty("product")]
	public string Product { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("data")]
	public FugleTicker[] Data { get; set; }
}

sealed class FugleTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("referencePrice")]
	public decimal? ReferencePrice { get; set; }

	[JsonProperty("contractType")]
	public string ContractType { get; set; }

	[JsonProperty("startDate")]
	public string StartDate { get; set; }

	[JsonProperty("endDate")]
	public string EndDate { get; set; }

	[JsonProperty("settlementDate")]
	public string SettlementDate { get; set; }

	[JsonProperty("isDynamicBanding")]
	public bool IsDynamicBanding { get; set; }
}

sealed class FugleStockTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }
}

sealed class FugleCandleResponse
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("timeframe")]
	public string TimeFrame { get; set; }

	[JsonProperty("data")]
	public FugleCandle[] Data { get; set; }
}

sealed class FugleCandle
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("open")]
	public decimal Open { get; set; }

	[JsonProperty("high")]
	public decimal High { get; set; }

	[JsonProperty("low")]
	public decimal Low { get; set; }

	[JsonProperty("close")]
	public decimal Close { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("average")]
	public decimal? Average { get; set; }
}

sealed class FugleErrorResponse
{
	[JsonProperty("statusCode")]
	public int? StatusCode { get; set; }

	[JsonProperty("code")]
	public string Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }
}

sealed class FugleSocketEnvelope
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }
}

sealed class FugleSocketRequest<TData>
	where TData : class
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class FugleAuthData
{
	[JsonProperty("apikey")]
	public string ApiKey { get; set; }
}

sealed class FugleSubscribeData
{
	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("intradayOddLot", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsIntradayOddLot { get; set; }

	[JsonProperty("afterHours", DefaultValueHandling = DefaultValueHandling.Ignore)]
	public bool IsAfterHours { get; set; }
}

sealed class FugleUnsubscribeData
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

sealed class FuglePingData
{
	[JsonProperty("state")]
	public string State { get; set; }
}

sealed class FugleSocketStatusMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public FugleSocketStatusData Data { get; set; }
}

sealed class FugleSocketStatusData
{
	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("state")]
	public string State { get; set; }
}

sealed class FugleSubscriptionMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("data")]
	public FugleSubscriptionData Data { get; set; }
}

sealed class FugleSubscriptionData
{
	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class FugleStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }

	[JsonProperty("id")]
	public string Id { get; set; }

	[JsonProperty("channel")]
	public string Channel { get; set; }

	[JsonProperty("data")]
	public FugleStreamData Data { get; set; }
}

sealed class FugleStreamData
{
	[JsonProperty("date")]
	public string Date { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("exchange")]
	public string Exchange { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("serial")]
	public string Serial { get; set; }

	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("price")]
	public decimal? Price { get; set; }

	[JsonProperty("size")]
	public decimal? Size { get; set; }

	[JsonProperty("volume")]
	public decimal? Volume { get; set; }

	[JsonProperty("index")]
	public decimal? Index { get; set; }

	[JsonProperty("referencePrice")]
	public decimal? ReferencePrice { get; set; }

	[JsonProperty("previousClose")]
	public decimal? PreviousClose { get; set; }

	[JsonProperty("openPrice")]
	public decimal? OpenPrice { get; set; }

	[JsonProperty("highPrice")]
	public decimal? HighPrice { get; set; }

	[JsonProperty("lowPrice")]
	public decimal? LowPrice { get; set; }

	[JsonProperty("closePrice")]
	public decimal? ClosePrice { get; set; }

	[JsonProperty("avgPrice")]
	public decimal? AveragePrice { get; set; }

	[JsonProperty("lastPrice")]
	public decimal? LastPrice { get; set; }

	[JsonProperty("lastSize")]
	public decimal? LastSize { get; set; }

	[JsonProperty("lastUpdated")]
	public long? LastUpdated { get; set; }

	[JsonProperty("open")]
	public decimal? Open { get; set; }

	[JsonProperty("high")]
	public decimal? High { get; set; }

	[JsonProperty("low")]
	public decimal? Low { get; set; }

	[JsonProperty("close")]
	public decimal? Close { get; set; }

	[JsonProperty("average")]
	public decimal? Average { get; set; }

	[JsonProperty("bids")]
	public FugleQuoteLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public FugleQuoteLevel[] Asks { get; set; }

	[JsonProperty("trades")]
	public FugleTrade[] Trades { get; set; }

	[JsonProperty("total")]
	public FugleTotal Total { get; set; }

	[JsonProperty("lastTrade")]
	public FugleTrade LastTrade { get; set; }
}

sealed class FugleQuoteLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }
}

sealed class FugleTrade
{
	[JsonProperty("bid")]
	public decimal? Bid { get; set; }

	[JsonProperty("ask")]
	public decimal? Ask { get; set; }

	[JsonProperty("price")]
	public decimal Price { get; set; }

	[JsonProperty("size")]
	public decimal Size { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }

	[JsonProperty("serial")]
	public string Serial { get; set; }
}

sealed class FugleTotal
{
	[JsonProperty("tradeValue")]
	public decimal? TradeValue { get; set; }

	[JsonProperty("tradeVolume")]
	public decimal? TradeVolume { get; set; }

	[JsonProperty("tradeVolumeAtBid")]
	public decimal? TradeVolumeAtBid { get; set; }

	[JsonProperty("tradeVolumeAtAsk")]
	public decimal? TradeVolumeAtAsk { get; set; }

	[JsonProperty("totalBidMatch")]
	public decimal? TotalBidMatch { get; set; }

	[JsonProperty("totalAskMatch")]
	public decimal? TotalAskMatch { get; set; }

	[JsonProperty("transaction")]
	public decimal? Transaction { get; set; }

	[JsonProperty("time")]
	public long? Time { get; set; }
}

sealed class FugleSubscription
{
	public string Channel { get; set; }
	public string Symbol { get; set; }
	public bool IsIntradayOddLot { get; set; }
	public bool IsAfterHours { get; set; }
	public long TransactionId { get; set; }
	public SecurityId SecurityId { get; set; }
	public string ServerId { get; set; }

	public string Key => $"{Channel?.ToLowerInvariant()}|{Symbol?.ToUpperInvariant()}|{IsIntradayOddLot}|{IsAfterHours}";
}
