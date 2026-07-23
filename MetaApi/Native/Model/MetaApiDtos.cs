namespace StockSharp.MetaApi.Native.Model;

sealed class MetaApiServerSettings
{
	public string Domain { get; set; }
	public string Hostname { get; set; }
}

sealed class MetaApiAccount
{
	[JsonProperty("_id")]
	public string Id { get; set; }
	public string Name { get; set; }
	public string Login { get; set; }
	public string Server { get; set; }
	public string State { get; set; }
	public string ConnectionStatus { get; set; }
	public string Type { get; set; }
	public string Region { get; set; }
	public string Reliability { get; set; }
	public string BaseCurrency { get; set; }
	public int Version { get; set; }
	public MetaApiAccountReplica[] AccountReplicas { get; set; }
}

sealed class MetaApiAccountReplica
{
	[JsonProperty("_id")]
	public string Id { get; set; }
	public string State { get; set; }
	public string ConnectionStatus { get; set; }
	public string Region { get; set; }
	public string Reliability { get; set; }
}

sealed class MetaApiAccountInformation
{
	public string Broker { get; set; }
	public string Currency { get; set; }
	public string Server { get; set; }
	public decimal Balance { get; set; }
	public decimal Equity { get; set; }
	public decimal Margin { get; set; }
	public decimal FreeMargin { get; set; }
	public decimal? MarginLevel { get; set; }
	public decimal? Leverage { get; set; }
	[JsonProperty("tradeAllowed")]
	public bool IsTradeAllowed { get; set; }
	public string MarginMode { get; set; }
	public string Name { get; set; }
	public string Login { get; set; }
	public decimal? Credit { get; set; }
	public decimal? AccountCurrencyExchangeRate { get; set; }
	public string Type { get; set; }
}

sealed class MetaApiSymbolSpecification
{
	public string Symbol { get; set; }
	public string Description { get; set; }
	public string Path { get; set; }
	public decimal TickSize { get; set; }
	public decimal MinVolume { get; set; }
	public decimal MaxVolume { get; set; }
	public decimal VolumeStep { get; set; }
	public decimal ContractSize { get; set; }
	public int? Digits { get; set; }
	public string BaseCurrency { get; set; }
	public string ProfitCurrency { get; set; }
	public string MarginCurrency { get; set; }
	public string TradeMode { get; set; }
	public string PriceCalculationMode { get; set; }
	public decimal? StopsLevel { get; set; }
	public decimal? FreezeLevel { get; set; }
}

sealed class MetaApiSymbolPrice
{
	public string Symbol { get; set; }
	public decimal Bid { get; set; }
	public decimal Ask { get; set; }
	public decimal? ProfitTickValue { get; set; }
	public decimal? LossTickValue { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
}

sealed class MetaApiCandle
{
	public string Symbol { get; set; }
	public string Timeframe { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public decimal Open { get; set; }
	public decimal High { get; set; }
	public decimal Low { get; set; }
	public decimal Close { get; set; }
	public decimal TickVolume { get; set; }
	public decimal? Spread { get; set; }
	public decimal? Volume { get; set; }
}

sealed class MetaApiTick
{
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public decimal Bid { get; set; }
	public decimal Ask { get; set; }
	public decimal? Last { get; set; }
	public decimal? Volume { get; set; }
	public string Side { get; set; }
}

sealed class MetaApiBook
{
	public string Symbol { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public MetaApiBookEntry[] Book { get; set; }
}

sealed class MetaApiBookEntry
{
	public string Type { get; set; }
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
}

sealed class MetaApiPosition
{
	public string Id { get; set; }
	public string Type { get; set; }
	public string Symbol { get; set; }
	public long? Magic { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public DateTime? UpdateTime { get; set; }
	public decimal OpenPrice { get; set; }
	public decimal CurrentPrice { get; set; }
	public decimal? CurrentTickValue { get; set; }
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
	public decimal Volume { get; set; }
	public decimal? Swap { get; set; }
	public decimal? Profit { get; set; }
	public decimal? Commission { get; set; }
	public decimal? UnrealizedProfit { get; set; }
	public decimal? RealizedProfit { get; set; }
	public string Comment { get; set; }
	public string ClientId { get; set; }
	public string Platform { get; set; }
}

sealed class MetaApiOrder
{
	public string Id { get; set; }
	public string Type { get; set; }
	public string State { get; set; }
	public string Symbol { get; set; }
	public long? Magic { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public DateTime? DoneTime { get; set; }
	public string DoneBrokerTime { get; set; }
	public DateTime? ExpirationTime { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? CurrentPrice { get; set; }
	public decimal Volume { get; set; }
	public decimal? CurrentVolume { get; set; }
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
	public string PositionId { get; set; }
	public string Reason { get; set; }
	public string FillingMode { get; set; }
	public string ExpirationType { get; set; }
	public string Comment { get; set; }
	public string ClientId { get; set; }
	public string Platform { get; set; }
}

sealed class MetaApiDeal
{
	public string Id { get; set; }
	public string Type { get; set; }
	public string EntryType { get; set; }
	public string Symbol { get; set; }
	public long? Magic { get; set; }
	public DateTime Time { get; set; }
	public string BrokerTime { get; set; }
	public decimal Volume { get; set; }
	public decimal Price { get; set; }
	public decimal? Commission { get; set; }
	public decimal? Swap { get; set; }
	public decimal? Profit { get; set; }
	public string PositionId { get; set; }
	public string OrderId { get; set; }
	public string Comment { get; set; }
	public string ClientId { get; set; }
	public string Platform { get; set; }
}

sealed class MetaApiTradeRequest
{
	public string ActionType { get; set; }
	public string Symbol { get; set; }
	public decimal? Volume { get; set; }
	public decimal? OpenPrice { get; set; }
	public decimal? StopLimitPrice { get; set; }
	public decimal? StopLoss { get; set; }
	public decimal? TakeProfit { get; set; }
	public string StopLossUnits { get; set; }
	public string TakeProfitUnits { get; set; }
	public string OrderId { get; set; }
	public string PositionId { get; set; }
	public string CloseByPositionId { get; set; }
	public string Comment { get; set; }
	public string ClientId { get; set; }
	public long? Magic { get; set; }
	public decimal? Slippage { get; set; }
	public string[] FillingModes { get; set; }
	public MetaApiExpiration Expiration { get; set; }
}

sealed class MetaApiExpiration
{
	public string Type { get; set; }
	public DateTime? Time { get; set; }
}

sealed class MetaApiTradeResponse
{
	public int NumericCode { get; set; }
	public string StringCode { get; set; }
	public string Message { get; set; }
	public string OrderId { get; set; }
	public string PositionId { get; set; }
}

sealed class MetaApiError
{
	public string Error { get; set; }
	public string Message { get; set; }
	public int? NumericCode { get; set; }
	public string StringCode { get; set; }
	public MetaApiRateLimitMetadata Metadata { get; set; }
}

sealed class MetaApiRateLimitMetadata
{
	public string Type { get; set; }
	public DateTime? RecommendedRetryTime { get; set; }
}

sealed class MetaApiEngineHandshake
{
	public string Sid { get; set; }
	public int PingInterval { get; set; }
	public int PingTimeout { get; set; }
}

sealed class MetaApiMarketDataSubscription
{
	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("timeframe")]
	public string Timeframe { get; set; }

	[JsonProperty("intervalInMilliseconds")]
	public int? IntervalInMilliseconds { get; set; }
}
