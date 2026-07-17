namespace StockSharp.TradeLocker.Native.Model;

internal sealed class TradeLockerLoginRequest
{
	[JsonProperty("email")]
	public string Email { get; set; }

	[JsonProperty("password")]
	public string Password { get; set; }

	[JsonProperty("server")]
	public string Server { get; set; }
}

internal sealed class TradeLockerRefreshRequest
{
	[JsonProperty("refreshToken")]
	public string RefreshToken { get; set; }
}

internal sealed class TradeLockerTokenResponse
{
	[JsonProperty("accessToken")]
	public string AccessToken { get; set; }

	[JsonProperty("refreshToken")]
	public string RefreshToken { get; set; }
}

internal sealed class TradeLockerAccountsResponse
{
	[JsonProperty("accounts")]
	public TradeLockerAccount[] Accounts { get; set; }
}

internal sealed class TradeLockerAccount
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("currency")]
	public string Currency { get; set; }

	[JsonProperty("accNum")]
	public int AccountNumber { get; set; }

	[JsonProperty("accountBalance")]
	public decimal Balance { get; set; }
}

internal sealed class TradeLockerConfigResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerConfig Data { get; set; }
}

internal sealed class TradeLockerConfig
{
	[JsonProperty("positionsConfig")]
	public TradeLockerColumnSet Positions { get; set; }

	[JsonProperty("ordersConfig")]
	public TradeLockerColumnSet Orders { get; set; }

	[JsonProperty("ordersHistoryConfig")]
	public TradeLockerColumnSet OrdersHistory { get; set; }

	[JsonProperty("accountDetailsConfig")]
	public TradeLockerColumnSet AccountDetails { get; set; }

	[JsonProperty("limits")]
	public TradeLockerLimit[] Limits { get; set; }
}

internal sealed class TradeLockerColumnSet
{
	[JsonProperty("columns")]
	public TradeLockerColumn[] Columns { get; set; }
}

internal sealed class TradeLockerColumn
{
	[JsonProperty("id")]
	public string Id { get; set; }
}

internal sealed class TradeLockerLimit
{
	[JsonProperty("limitType")]
	public string Type { get; set; }

	[JsonProperty("limit")]
	public int Limit { get; set; }
}

internal sealed class TradeLockerInstrumentsResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerInstrumentsData Data { get; set; }
}

internal sealed class TradeLockerInstrumentsData
{
	[JsonProperty("instruments")]
	public TradeLockerInstrument[] Instruments { get; set; }
}

internal sealed class TradeLockerInstrument
{
	[JsonProperty("tradableInstrumentId")]
	public long TradableId { get; set; }

	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("name")]
	public string Name { get; set; }

	[JsonProperty("description")]
	public string Description { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("tradingExchange")]
	public string TradingExchange { get; set; }

	[JsonProperty("marketDataExchange")]
	public string MarketDataExchange { get; set; }

	[JsonProperty("country")]
	public string Country { get; set; }

	[JsonProperty("routes")]
	public TradeLockerRoute[] Routes { get; set; }

	[JsonProperty("hasIntraday")]
	public bool HasIntraday { get; set; }

	[JsonProperty("hasDaily")]
	public bool HasDaily { get; set; }
}

internal sealed class TradeLockerRoute
{
	[JsonProperty("id")]
	public long Id { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }
}

internal sealed class TradeLockerInstrumentResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerInstrumentDetails Data { get; set; }
}

internal sealed class TradeLockerInstrumentDetails
{
	[JsonProperty("pricePrecision")]
	public int PricePrecision { get; set; }

	[JsonProperty("lotSize")]
	public decimal? LotSize { get; set; }

	[JsonProperty("lotStep")]
	public decimal? LotStep { get; set; }

	[JsonProperty("quoteCurrency")]
	public string QuoteCurrency { get; set; }
}

internal sealed class TradeLockerQuotesResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerQuote Data { get; set; }
}

internal sealed class TradeLockerQuote
{
	[JsonProperty("ap")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("bp")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("as")]
	public decimal? AskSize { get; set; }

	[JsonProperty("bs")]
	public decimal? BidSize { get; set; }
}

internal sealed class TradeLockerHistoryResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerHistoryData Data { get; set; }
}

internal sealed class TradeLockerHistoryData
{
	[JsonProperty("barDetails")]
	public TradeLockerBar[] Bars { get; set; }
}

internal sealed class TradeLockerBar
{
	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }
}

internal sealed class TradeLockerOrderRequest
{
	[JsonProperty("price", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? Price { get; set; }

	[JsonProperty("qty")]
	public decimal Quantity { get; set; }

	[JsonProperty("routeId")]
	public long RouteId { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("validity")]
	public string Validity { get; set; }

	[JsonProperty("tradableInstrumentId")]
	public long TradableInstrumentId { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("stopPrice", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopPrice { get; set; }

	[JsonProperty("stopLoss", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? StopLoss { get; set; }

	[JsonProperty("stopLossType", NullValueHandling = NullValueHandling.Ignore)]
	public string StopLossType { get; set; }

	[JsonProperty("takeProfit", NullValueHandling = NullValueHandling.Ignore)]
	public decimal? TakeProfit { get; set; }

	[JsonProperty("takeProfitType", NullValueHandling = NullValueHandling.Ignore)]
	public string TakeProfitType { get; set; }

	[JsonProperty("strategyId", NullValueHandling = NullValueHandling.Ignore)]
	public string StrategyId { get; set; }
}

internal sealed class TradeLockerOrderResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("d")]
	public TradeLockerOrderResult Data { get; set; }
}

internal sealed class TradeLockerOrderResult
{
	[JsonProperty("orderId")]
	public long OrderId { get; set; }
}

internal sealed class TradeLockerClosePositionRequest
{
	[JsonProperty("qty")]
	public decimal Quantity { get; set; }
}

internal sealed class TradeLockerStatusResponse
{
	[JsonProperty("s")]
	public string Status { get; set; }
}

internal sealed class TradeLockerOrder
{
	public long Id { get; set; }
	public long TradableInstrumentId { get; set; }
	public long RouteId { get; set; }
	public decimal Quantity { get; set; }
	public string Side { get; set; }
	public string Type { get; set; }
	public string Status { get; set; }
	public decimal FilledQuantity { get; set; }
	public decimal AveragePrice { get; set; }
	public decimal Price { get; set; }
	public decimal StopPrice { get; set; }
	public string Validity { get; set; }
	public long ExpireDate { get; set; }
	public long CreatedDate { get; set; }
	public long LastModified { get; set; }
	public bool IsOpen { get; set; }
	public long PositionId { get; set; }
}

internal sealed class TradeLockerPosition
{
	public long Id { get; set; }
	public long TradableInstrumentId { get; set; }
	public long RouteId { get; set; }
	public string Side { get; set; }
	public decimal Quantity { get; set; }
	public decimal AveragePrice { get; set; }
	public long OpenDate { get; set; }
	public decimal UnrealizedPnL { get; set; }
}

internal sealed class TradeLockerAccountState
{
	public decimal Balance { get; set; }
	public decimal AvailableFunds { get; set; }
	public decimal CashBalance { get; set; }
	public decimal InitialMarginRequirement { get; set; }
	public decimal MaintenanceMarginRequirement { get; set; }
	public decimal OpenNetPnL { get; set; }
}
