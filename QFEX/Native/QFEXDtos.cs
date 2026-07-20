namespace StockSharp.QFEX.Native;

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXMarketMessageTypes
{
	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,

	[EnumMember(Value = "level2")]
	Level2,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "bbo")]
	BestBidOffer,

	[EnumMember(Value = "underlier")]
	Underlier,

	[EnumMember(Value = "mark_price")]
	MarkPrice,

	[EnumMember(Value = "funding")]
	Funding,

	[EnumMember(Value = "candle")]
	Candle,

	[EnumMember(Value = "open_interest")]
	OpenInterest,

	[EnumMember(Value = "minmax_price")]
	MinimumMaximumPrice,

	[EnumMember(Value = "market_stats")]
	MarketStatistics,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXMarketChannels
{
	[EnumMember(Value = "level2")]
	Level2,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "bbo")]
	BestBidOffer,

	[EnumMember(Value = "underlier")]
	Underlier,

	[EnumMember(Value = "mark_price")]
	MarkPrice,

	[EnumMember(Value = "funding")]
	Funding,

	[EnumMember(Value = "candle")]
	Candle,

	[EnumMember(Value = "open_interest")]
	OpenInterest,

	[EnumMember(Value = "minmax_price")]
	MinimumMaximumPrice,

	[EnumMember(Value = "market_stats")]
	MarketStatistics,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXSubscriptionActions
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXCandleIntervals
{
	[EnumMember(Value = "1MIN")]
	OneMinute,

	[EnumMember(Value = "5MINS")]
	FiveMinutes,

	[EnumMember(Value = "15MINS")]
	FifteenMinutes,

	[EnumMember(Value = "1HOUR")]
	OneHour,

	[EnumMember(Value = "4HOURS")]
	FourHours,

	[EnumMember(Value = "1DAY")]
	OneDay,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXOrderDirections
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXOrderTypes
{
	[EnumMember(Value = "LIMIT")]
	Limit,

	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "ALO")]
	AddLiquidityOnly,

	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,

	[EnumMember(Value = "STOP_LOSS")]
	StopLoss,

	[EnumMember(Value = "STOP_MARKET")]
	StopMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXOrderStatuses
{
	[EnumMember(Value = "ACK")]
	Acknowledged,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "MODIFIED")]
	Modified,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "CANCELLED_STP")]
	CancelledSelfTradePrevention,

	[EnumMember(Value = "REJECTED")]
	Rejected,

	[EnumMember(Value = "NO_SUCH_ORDER")]
	NoSuchOrder,

	[EnumMember(Value = "INVALID_ORDER_TYPE")]
	InvalidOrderType,

	[EnumMember(Value = "BAD_SYMBOL")]
	BadSymbol,

	[EnumMember(Value = "PRICE_LESS_THAN_MIN_PRICE")]
	PriceBelowMinimum,

	[EnumMember(Value = "PRICE_GREATER_THAN_MAX_PRICE")]
	PriceAboveMaximum,

	[EnumMember(Value = "CANNOT_MODIFY_PARTIAL_FILL")]
	CannotModifyPartialFill,

	[EnumMember(Value = "CANNOT_MODIFY_NO_SUCH_ORDER")]
	CannotModifyMissingOrder,

	[EnumMember(Value = "CANNOT_MODIFY_ALO_WOULD_CROSS")]
	CannotModifyAddLiquidityOnlyWouldCross,

	[EnumMember(Value = "FAILED_MARGIN_CHECK")]
	FailedMarginCheck,

	[EnumMember(Value = "INVALID_TICK_SIZE_PRECISION_PRICE")]
	InvalidPricePrecision,

	[EnumMember(Value = "INVALID_TICK_SIZE_PRECISION_QUANTITY")]
	InvalidQuantityPrecision,

	[EnumMember(Value = "QUANTITY_LESS_THAN_MIN_QUANTITY")]
	QuantityBelowMinimum,

	[EnumMember(Value = "QUANTITY_GREATER_THAN_MAX_QUANTITY")]
	QuantityAboveMaximum,

	[EnumMember(Value = "INVALID_TIME_IN_FORCE")]
	InvalidTimeInForce,

	[EnumMember(Value = "REJECTED_WOULD_BREACH_MAX_NOTIONAL")]
	RejectedMaximumNotional,

	[EnumMember(Value = "REJECTED_MARKET_CLOSED")]
	RejectedMarketClosed,

	[EnumMember(Value = "REJECTED_FAILED_TO_PROCESS")]
	RejectedProcessingFailure,

	[EnumMember(Value = "INVALID_TAKEPROFIT_PRICE")]
	InvalidTakeProfitPrice,

	[EnumMember(Value = "INVALID_STOPLOSS_PRICE")]
	InvalidStopLossPrice,

	[EnumMember(Value = "RATE_LIMITED")]
	RateLimited,

	[EnumMember(Value = "REJECTED_TOO_MANY_OPEN_ORDERS")]
	RejectedTooManyOpenOrders,

	[EnumMember(Value = "REJECTED_OPEN_INTEREST_LIMIT")]
	RejectedOpenInterestLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXSymbolStatuses
{
	[EnumMember(Value = "ACTIVE")]
	Active,

	[EnumMember(Value = "INACTIVE")]
	Inactive,

	[EnumMember(Value = "DELISTED")]
	Delisted,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXProductCategories
{
	[EnumMember(Value = "EQUITY")]
	Equity,

	[EnumMember(Value = "INDEX")]
	Index,

	[EnumMember(Value = "COMMODITY")]
	Commodity,

	[EnumMember(Value = "FX")]
	ForeignExchange,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXTradeRequestTypes
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "add_order")]
	AddOrder,

	[EnumMember(Value = "cancel_order")]
	CancelOrder,

	[EnumMember(Value = "modify_order")]
	ModifyOrder,

	[EnumMember(Value = "cancel_all_orders")]
	CancelAllOrders,

	[EnumMember(Value = "get_user_orders")]
	GetUserOrders,

	[EnumMember(Value = "get_user_trades")]
	GetUserTrades,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXTradeChannels
{
	[EnumMember(Value = "order_responses")]
	OrderResponses,

	[EnumMember(Value = "positions")]
	Positions,

	[EnumMember(Value = "balances")]
	Balances,

	[EnumMember(Value = "fills")]
	Fills,

	[EnumMember(Value = "stop_orders")]
	StopOrders,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXTradeEnvelopeTypes
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "balance_update")]
	BalanceUpdate,

	[EnumMember(Value = "position_update")]
	PositionUpdate,

	[EnumMember(Value = "v4_balances")]
	Balances,

	[EnumMember(Value = "v4_positions")]
	Positions,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXTradeStreamChannels
{
	[EnumMember(Value = "v4_balances")]
	Balances,

	[EnumMember(Value = "v4_positions")]
	Positions,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXCancelOrderIdTypes
{
	[EnumMember(Value = "order_id")]
	OrderId,

	[EnumMember(Value = "client_order_id")]
	ClientOrderId,
}

[JsonConverter(typeof(StringEnumConverter))]
enum QFEXErrorCodes
{
	[EnumMember(Value = "RateLimited")]
	RateLimited,

	[EnumMember(Value = "InvalidJSONFormat")]
	InvalidJsonFormat,

	[EnumMember(Value = "AlreadyAuthenticated")]
	AlreadyAuthenticated,

	[EnumMember(Value = "InvalidParameter")]
	InvalidParameter,

	[EnumMember(Value = "PermissionDenied")]
	PermissionDenied,

	[EnumMember(Value = "ServerError")]
	ServerError,

	[EnumMember(Value = "InvalidOrder")]
	InvalidOrder,

	[EnumMember(Value = "KycRequired")]
	KycRequired,

	[EnumMember(Value = "TncRequired")]
	TermsAndConditionsRequired,
}

sealed class QFEXReferenceDataResponse
{
	[JsonProperty("type")]
	public string Type { get; init; }

	[JsonProperty("data")]
	public QFEXReferenceDataSymbol[] Data { get; init; }
}

sealed class QFEXReferenceDataSymbol
{
	[JsonProperty("clobPairId")]
	public string PairId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("underlier_price")]
	public string UnderlierPrice { get; init; }

	[JsonProperty("price_change_24h")]
	public string PriceChange24Hours { get; init; }

	[JsonProperty("default_max_leverage")]
	public long DefaultMaximumLeverage { get; init; }

	[JsonProperty("tick_size")]
	public string TickSize { get; init; }

	[JsonProperty("lot_size")]
	public string LotSize { get; init; }

	[JsonProperty("min_price")]
	public string MinimumPrice { get; init; }

	[JsonProperty("max_price")]
	public string MaximumPrice { get; init; }

	[JsonProperty("min_quantity")]
	public string MinimumQuantity { get; init; }

	[JsonProperty("max_quantity")]
	public string MaximumQuantity { get; init; }

	[JsonProperty("base_asset")]
	public string BaseAsset { get; init; }

	[JsonProperty("quote_asset")]
	public string QuoteAsset { get; init; }

	[JsonProperty("margin_asset")]
	public string MarginAsset { get; init; }

	[JsonProperty("order_time_in_force")]
	public QFEXTimeInForces[] TimeInForces { get; init; }

	[JsonProperty("order_types")]
	public QFEXOrderTypes[] OrderTypes { get; init; }

	[JsonProperty("status")]
	public QFEXSymbolStatuses Status { get; init; }

	[JsonProperty("product_category")]
	public QFEXProductCategories ProductCategory { get; init; }
}

sealed class QFEXCandlesResponse
{
	[JsonProperty("candles")]
	public QFEXCandle[] Candles { get; init; }
}

sealed class QFEXCandle
{
	[JsonProperty("startedAt")]
	public string StartedAt { get; init; }

	[JsonProperty("start")]
	public string Start { get; init; }

	[JsonProperty("ticker")]
	public string Ticker { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("resolution")]
	public QFEXCandleIntervals Resolution { get; init; }

	[JsonProperty("open")]
	public string Open { get; init; }

	[JsonProperty("high")]
	public string High { get; init; }

	[JsonProperty("low")]
	public string Low { get; init; }

	[JsonProperty("close")]
	public string Close { get; init; }

	[JsonProperty("baseTokenVolume")]
	public string BaseTokenVolume { get; init; }

	[JsonProperty("usdVolume")]
	public string UsdVolume { get; init; }

	[JsonProperty("trades")]
	public string Trades { get; init; }

	[JsonProperty("startingOpenInterest")]
	public string StartingOpenInterest { get; init; }
}

sealed class QFEXMarketSubscriptionRequest
{
	[JsonProperty("type")]
	public QFEXSubscriptionActions Action { get; init; }

	[JsonProperty("channels")]
	public QFEXMarketChannels[] Channels { get; init; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; init; }

	[JsonProperty("intervals")]
	public QFEXCandleIntervals[] Intervals { get; init; }
}

sealed class QFEXMarketDataMessage
{
	[JsonProperty("type")]
	public QFEXMarketMessageTypes Type { get; init; }

	[JsonProperty("sequence")]
	public long? Sequence { get; init; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; init; }

	[JsonProperty("message_id")]
	public long? MessageId { get; init; }

	[JsonProperty("channel")]
	public string Channel { get; init; }

	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("channels")]
	public QFEXMarketChannels[] Channels { get; init; }

	[JsonProperty("symbols")]
	public string[] Symbols { get; init; }

	[JsonProperty("intervals")]
	public QFEXCandleIntervals[] Intervals { get; init; }

	[JsonProperty("contents")]
	public QFEXMarketDataMessage[] Contents { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("time")]
	public string Time { get; init; }

	[JsonProperty("bid")]
	public string[][] Bids { get; init; }

	[JsonProperty("ask")]
	public string[][] Asks { get; init; }

	[JsonProperty("sig_figs")]
	public int? SignificantFigures { get; init; }

	[JsonProperty("trade_id")]
	public string TradeId { get; init; }

	[JsonProperty("size")]
	public string Size { get; init; }

	[JsonProperty("price")]
	public string Price { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections? Side { get; init; }

	[JsonProperty("execution_type")]
	public string ExecutionType { get; init; }

	[JsonProperty("start")]
	public string Start { get; init; }

	[JsonProperty("resolution")]
	public QFEXCandleIntervals? Resolution { get; init; }

	[JsonProperty("open")]
	public string Open { get; init; }

	[JsonProperty("high")]
	public string High { get; init; }

	[JsonProperty("low")]
	public string Low { get; init; }

	[JsonProperty("close")]
	public string Close { get; init; }

	[JsonProperty("usdVolume")]
	public string UsdVolume { get; init; }

	[JsonProperty("trades")]
	public string Trades { get; init; }

	[JsonProperty("funding_rate")]
	public string FundingRate { get; init; }

	[JsonProperty("annualised_funding_rate")]
	public string AnnualizedFundingRate { get; init; }

	[JsonProperty("time_remaining")]
	public string TimeRemaining { get; init; }

	[JsonProperty("open_interest")]
	public string OpenInterest { get; init; }

	[JsonProperty("min_price")]
	public string MinimumPrice { get; init; }

	[JsonProperty("max_price")]
	public string MaximumPrice { get; init; }

	[JsonProperty("source")]
	public string Source { get; init; }

	[JsonProperty("err")]
	public QFEXError Error { get; init; }
}

sealed class QFEXTradeRequest<TParameters>
{
	[JsonProperty("type")]
	public QFEXTradeRequestTypes Type { get; init; }

	[JsonProperty("params")]
	public TParameters Parameters { get; init; }
}

sealed class QFEXAuthenticationParameters
{
	[JsonProperty("hmac")]
	public QFEXHmacCredentials Hmac { get; init; }

	[JsonProperty("account_id")]
	public string AccountId { get; init; }
}

sealed class QFEXHmacCredentials
{
	[JsonProperty("public_key")]
	public string PublicKey { get; init; }

	[JsonProperty("nonce")]
	public string Nonce { get; init; }

	[JsonProperty("unix_ts")]
	public long UnixTimestamp { get; init; }

	[JsonProperty("signature")]
	public string Signature { get; init; }
}

sealed class QFEXTradeSubscriptionParameters
{
	[JsonProperty("channels")]
	public QFEXTradeChannels[] Channels { get; init; }
}

sealed class QFEXAddOrderParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("order_type")]
	public QFEXOrderTypes OrderType { get; init; }

	[JsonProperty("order_time_in_force")]
	public QFEXTimeInForces TimeInForce { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("reduce_only")]
	public bool IsReduceOnly { get; init; }

	[JsonProperty("take_profit")]
	public decimal? TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public decimal? StopLoss { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }
}

sealed class QFEXCancelOrderParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("cancel_order_id_type")]
	public QFEXCancelOrderIdTypes OrderIdType { get; init; }
}

sealed class QFEXModifyOrderParameters
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("price")]
	public decimal? Price { get; init; }

	[JsonProperty("quantity")]
	public decimal? Quantity { get; init; }

	[JsonProperty("take_profit")]
	public decimal? TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public decimal? StopLoss { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("order_type")]
	public QFEXOrderTypes OrderType { get; init; }
}

sealed class QFEXCancelAllOrdersParameters
{
}

sealed class QFEXGetUserOrdersParameters
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("offset")]
	public int Offset { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class QFEXGetUserTradesParameters
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("offset")]
	public int Offset { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("start_ts")]
	public long? StartTimestamp { get; init; }

	[JsonProperty("end_ts")]
	public long? EndTimestamp { get; init; }
}

sealed class QFEXTradeMessage
{
	[JsonProperty("type")]
	public QFEXTradeEnvelopeTypes? Type { get; init; }

	[JsonProperty("result")]
	public string Result { get; init; }

	[JsonProperty("connection_id")]
	public string ConnectionId { get; init; }

	[JsonProperty("message_id")]
	public long? MessageId { get; init; }

	[JsonProperty("channel")]
	public QFEXTradeStreamChannels? Channel { get; init; }

	[JsonProperty("contents")]
	public QFEXTradeStreamItem[] Contents { get; init; }

	[JsonProperty("authenticated")]
	public bool? IsAuthenticated { get; init; }

	[JsonProperty("subscribed")]
	public QFEXTradeChannels? Subscribed { get; init; }

	[JsonProperty("unsubscribed")]
	public QFEXTradeChannels? Unsubscribed { get; init; }

	[JsonProperty("order_response")]
	public QFEXOrder OrderResponse { get; init; }

	[JsonProperty("fill_response")]
	public QFEXFill FillResponse { get; init; }

	[JsonProperty("all_orders_response")]
	public QFEXAllOrdersResponse AllOrdersResponse { get; init; }

	[JsonProperty("user_trades_response")]
	public QFEXUserTrade[] UserTradesResponse { get; init; }

	[JsonProperty("position_response")]
	public QFEXPosition PositionResponse { get; init; }

	[JsonProperty("balance_response")]
	public QFEXBalance BalanceResponse { get; init; }

	[JsonProperty("ack")]
	public QFEXAcknowledgement Acknowledgement { get; init; }

	[JsonProperty("err")]
	public QFEXError Error { get; init; }

	[JsonProperty("error_code")]
	public QFEXErrorCodes? ErrorCode { get; init; }

	[JsonProperty("message")]
	public string ErrorMessage { get; init; }
}

sealed class QFEXTradeStreamItem
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("user_id")]
	public string UserId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("deposit")]
	public decimal Deposit { get; init; }

	[JsonProperty("realised_pnl")]
	public decimal RealizedProfitLoss { get; init; }

	[JsonProperty("unrealised_pnl")]
	public decimal UnrealizedProfitLoss { get; init; }

	[JsonProperty("order_margin")]
	public decimal OrderMargin { get; init; }

	[JsonProperty("position_margin")]
	public decimal PositionMargin { get; init; }

	[JsonProperty("available_balance")]
	public decimal AvailableBalance { get; init; }

	[JsonProperty("net_funding")]
	public decimal NetFunding { get; init; }

	[JsonProperty("fees")]
	public decimal Fees { get; init; }

	[JsonProperty("position")]
	public decimal Position { get; init; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; init; }

	[JsonProperty("initial_margin")]
	public decimal InitialMargin { get; init; }

	[JsonProperty("maintenance_margin")]
	public decimal MaintenanceMargin { get; init; }

	[JsonProperty("open_orders")]
	public decimal OpenOrders { get; init; }

	[JsonProperty("open_quantity")]
	public decimal OpenQuantity { get; init; }

	[JsonProperty("margin_alloc")]
	public decimal MarginAllocation { get; init; }
}

sealed class QFEXAllOrdersResponse
{
	[JsonProperty("orders")]
	public QFEXOrder[] Orders { get; init; }
}

sealed class QFEXOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("status")]
	public QFEXOrderStatuses Status { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("take_profit")]
	public decimal? TakeProfit { get; init; }

	[JsonProperty("stop_loss")]
	public decimal? StopLoss { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("type")]
	public QFEXOrderTypes OrderType { get; init; }

	[JsonProperty("time_in_force")]
	public QFEXTimeInForces TimeInForce { get; init; }

	[JsonProperty("user_id")]
	public string UserId { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("quantity_remaining")]
	public decimal QuantityRemaining { get; init; }

	[JsonProperty("update_time")]
	public decimal UpdateTime { get; init; }

	[JsonProperty("trade_id")]
	public string TradeId { get; init; }
}

sealed class QFEXFill
{
	[JsonProperty("trade_id")]
	public string TradeId { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("fee")]
	public decimal Fee { get; init; }

	[JsonProperty("order_type")]
	public QFEXOrderTypes OrderType { get; init; }

	[JsonProperty("tif")]
	public QFEXTimeInForces TimeInForce { get; init; }

	[JsonProperty("order_price")]
	public decimal OrderPrice { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("remaining_quantity")]
	public decimal RemainingQuantity { get; init; }

	[JsonProperty("realised_pnl")]
	public decimal RealizedProfitLoss { get; init; }

	[JsonProperty("timestamp")]
	public decimal Timestamp { get; init; }
}

sealed class QFEXBalance
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("user_id")]
	public string UserId { get; init; }

	[JsonProperty("deposit")]
	public decimal Deposit { get; init; }

	[JsonProperty("realised_pnl")]
	public decimal RealizedProfitLoss { get; init; }

	[JsonProperty("unrealised_pnl")]
	public decimal UnrealizedProfitLoss { get; init; }

	[JsonProperty("order_margin")]
	public decimal OrderMargin { get; init; }

	[JsonProperty("position_margin")]
	public decimal PositionMargin { get; init; }

	[JsonProperty("available_balance")]
	public decimal AvailableBalance { get; init; }

	[JsonProperty("net_funding")]
	public decimal NetFunding { get; init; }

	[JsonProperty("fees")]
	public decimal Fees { get; init; }
}

sealed class QFEXPosition
{
	[JsonProperty("id")]
	public string Id { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("position")]
	public decimal Position { get; init; }

	[JsonProperty("average_price")]
	public decimal AveragePrice { get; init; }

	[JsonProperty("realised_pnl")]
	public decimal RealizedProfitLoss { get; init; }

	[JsonProperty("unrealised_pnl")]
	public decimal UnrealizedProfitLoss { get; init; }

	[JsonProperty("net_funding")]
	public decimal NetFunding { get; init; }

	[JsonProperty("leverage")]
	public decimal Leverage { get; init; }

	[JsonProperty("initial_margin")]
	public decimal InitialMargin { get; init; }

	[JsonProperty("maintenance_margin")]
	public decimal MaintenanceMargin { get; init; }

	[JsonProperty("open_orders")]
	public decimal OpenOrders { get; init; }

	[JsonProperty("open_quantity")]
	public decimal OpenQuantity { get; init; }

	[JsonProperty("margin_alloc")]
	public decimal MarginAllocation { get; init; }
}

sealed class QFEXPortfolioResponse
{
	[JsonProperty("balance")]
	public QFEXBalance Balance { get; init; }

	[JsonProperty("positions")]
	public QFEXPosition[] Positions { get; init; }
}

sealed class QFEXHistoricOrdersResponse
{
	[JsonProperty("data")]
	public QFEXHistoricOrder[] Data { get; init; }

	[JsonProperty("count")]
	public long Count { get; init; }
}

sealed class QFEXHistoricOrder
{
	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("terminal_status")]
	public QFEXOrderStatuses Status { get; init; }

	[JsonProperty("status_ts")]
	public string StatusTime { get; init; }

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("type")]
	public QFEXOrderTypes OrderType { get; init; }

	[JsonProperty("time_in_force")]
	public QFEXTimeInForces TimeInForce { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("client_order_id")]
	public string ClientOrderId { get; init; }

	[JsonProperty("filled_qty")]
	public decimal FilledQuantity { get; init; }

	[JsonProperty("avg_price")]
	public decimal? AveragePrice { get; init; }
}

sealed class QFEXUserTradesResponse
{
	[JsonProperty("data")]
	public QFEXUserTrade[] Data { get; init; }

	[JsonProperty("count")]
	public long Count { get; init; }
}

sealed class QFEXUserTrade
{
	[JsonProperty("id")]
	public string TradeId { get; init; }

	[JsonProperty("trade_id")]
	private string AlternateTradeId
	{
		init
		{
			if (TradeId.IsEmpty())
				TradeId = value;
		}
	}

	[JsonProperty("order_timestamp")]
	public decimal Timestamp { get; init; }

	[JsonProperty("timestamp")]
	private decimal AlternateTimestamp
	{
		init
		{
			if (Timestamp == 0)
				Timestamp = value;
		}
	}

	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("quantity")]
	public decimal Quantity { get; init; }

	[JsonProperty("side")]
	public QFEXOrderDirections Side { get; init; }

	[JsonProperty("fee")]
	public decimal Fee { get; init; }

	[JsonProperty("order_type")]
	public QFEXOrderTypes OrderType { get; init; }

	[JsonProperty("tif")]
	public QFEXTimeInForces TimeInForce { get; init; }

	[JsonProperty("order_id")]
	public string OrderId { get; init; }

	[JsonProperty("realised_pnl_change")]
	public decimal RealizedProfitLoss { get; init; }
}

sealed class QFEXAcknowledgement
{
}

sealed class QFEXError
{
	[JsonProperty("error_code")]
	public QFEXErrorCodes ErrorCode { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
