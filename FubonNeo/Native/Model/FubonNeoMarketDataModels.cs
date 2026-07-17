namespace StockSharp.FubonNeo.Native.Model;

sealed class FubonNeoTickerListResponse
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
	public FubonNeoTicker[] Data { get; set; }
}

sealed class FubonNeoTicker
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
}

sealed class FubonNeoCandleResponse
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
	public FubonNeoCandle[] Data { get; set; }
}

sealed class FubonNeoCandle
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

sealed class FubonNeoSocketEnvelope
{
	[JsonProperty("event")]
	public string Event { get; set; }
	[JsonProperty("id")]
	public string Id { get; set; }
	[JsonProperty("channel")]
	public string Channel { get; set; }
}

sealed class FubonNeoSocketStatusMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }
	[JsonProperty("data")]
	public FubonNeoSocketStatusData Data { get; set; }
}

sealed class FubonNeoSocketStatusData
{
	[JsonProperty("message")]
	public string Message { get; set; }
	[JsonProperty("time")]
	public long? Time { get; set; }
	[JsonProperty("state")]
	public string State { get; set; }
}

sealed class FubonNeoSubscriptionMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }
	[JsonProperty("data")]
	public FubonNeoSubscriptionData Data { get; set; }
}

sealed class FubonNeoSubscriptionData
{
	[JsonProperty("id")]
	public string Id { get; set; }
	[JsonProperty("channel")]
	public string Channel { get; set; }
	[JsonProperty("symbol")]
	public string Symbol { get; set; }
}

sealed class FubonNeoStreamMessage
{
	[JsonProperty("event")]
	public string Event { get; set; }
	[JsonProperty("id")]
	public string Id { get; set; }
	[JsonProperty("channel")]
	public string Channel { get; set; }
	[JsonProperty("data")]
	public FubonNeoStreamData Data { get; set; }
}

sealed class FubonNeoStreamData
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
	public FubonNeoQuoteLevel[] Bids { get; set; }
	[JsonProperty("asks")]
	public FubonNeoQuoteLevel[] Asks { get; set; }
	[JsonProperty("trades")]
	public FubonNeoTrade[] Trades { get; set; }
	[JsonProperty("total")]
	public FubonNeoTotal Total { get; set; }
	[JsonProperty("lastTrade")]
	public FubonNeoTrade LastTrade { get; set; }
}

sealed class FubonNeoQuoteLevel
{
	[JsonProperty("price")]
	public decimal Price { get; set; }
	[JsonProperty("size")]
	public decimal Size { get; set; }
}

sealed class FubonNeoTrade
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

sealed class FubonNeoTotal
{
	[JsonProperty("tradeValue")]
	public decimal? TradeValue { get; set; }
	[JsonProperty("tradeVolume")]
	public decimal? TradeVolume { get; set; }
	[JsonProperty("transaction")]
	public decimal? Transaction { get; set; }
	[JsonProperty("time")]
	public long? Time { get; set; }
}
