namespace StockSharp.VALR.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum VALRSocketCommands
{
	[EnumMember(Value = "SUBSCRIBE")]
	Subscribe,

	[EnumMember(Value = "UNSUBSCRIBE")]
	Unsubscribe,

	[EnumMember(Value = "PING")]
	Ping,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRSocketEvents
{
	[EnumMember(Value = "MARKET_SUMMARY_UPDATE")]
	MarketSummary,

	[EnumMember(Value = "AGGREGATED_ORDERBOOK_UPDATE")]
	OrderBook,

	[EnumMember(Value = "NEW_TRADE")]
	Trade,

	[EnumMember(Value = "NEW_TRADE_BUCKET")]
	TradeBucket,

	[EnumMember(Value = "MARGIN_INFO")]
	MarginInfo,
}

[JsonConverter(typeof(StringEnumConverter))]
enum VALRSocketMessageTypes
{
	[EnumMember(Value = "PONG")]
	Pong,

	[EnumMember(Value = "RATE_LIMIT_EXCEEDED")]
	RateLimitExceeded,

	[EnumMember(Value = "MARKET_SUMMARY_UPDATE")]
	MarketSummary,

	[EnumMember(Value = "AGGREGATED_ORDERBOOK_UPDATE")]
	OrderBook,

	[EnumMember(Value = "NEW_TRADE")]
	Trade,

	[EnumMember(Value = "NEW_TRADE_BUCKET")]
	TradeBucket,

	[EnumMember(Value = "BALANCE_UPDATE")]
	Balance,

	[EnumMember(Value = "OPEN_ORDERS_UPDATE")]
	OpenOrders,

	[EnumMember(Value = "ORDER_STATUS_UPDATE")]
	OrderStatus,

	[EnumMember(Value = "NEW_ACCOUNT_TRADE")]
	AccountTrade,

	[EnumMember(Value = "OPEN_POSITION_UPDATE")]
	OpenPosition,

	[EnumMember(Value = "POSITION_CLOSED")]
	PositionClosed,

	[EnumMember(Value = "MARGIN_INFO")]
	MarginInfo,

	[EnumMember(Value = "ORDER_PROCESSED")]
	OrderProcessed,

	[EnumMember(Value = "FAILED_CANCEL_ORDER")]
	FailedCancelOrder,

	[EnumMember(Value = "NEW_ACCOUNT_HISTORY_RECORD")]
	AccountHistory,

	[EnumMember(Value = "INSTANT_ORDER_COMPLETED")]
	InstantOrderCompleted,

	[EnumMember(Value = "REDUCE_POSITION")]
	ReducePosition,

	[EnumMember(Value = "ADD_CONDITIONAL_ORDER")]
	AddConditionalOrder,

	[EnumMember(Value = "REMOVE_CONDITIONAL_ORDER")]
	RemoveConditionalOrder,

	[EnumMember(Value = "MODIFY_ORDER_OUTCOME")]
	ModifyOrderOutcome,

	[EnumMember(Value = "NEW_PENDING_RECEIVE")]
	NewPendingReceive,

	[EnumMember(Value = "SEND_STATUS_UPDATE")]
	SendStatusUpdate,

	[EnumMember(Value = "LEVERAGE_UPDATED")]
	LeverageUpdated,

	[EnumMember(Value = "PLACE_LIMIT_WS_RESPONSE")]
	PlaceLimitResponse,

	[EnumMember(Value = "PLACE_MARKET_WS_RESPONSE")]
	PlaceMarketResponse,

	[EnumMember(Value = "MODIFY_ORDER_WS_RESPONSE")]
	ModifyOrderResponse,

	[EnumMember(Value = "CANCEL_ORDER_WS_RESPONSE")]
	CancelOrderResponse,

	[EnumMember(Value = "CANCEL_ON_DISCONNECT_UPDATE")]
	CancelOnDisconnect,
}

sealed class VALRSocketSubscription
{
	[JsonProperty("event")]
	public VALRSocketEvents Event { get; init; }

	[JsonProperty("pairs")]
	public string[] Pairs { get; init; }
}

sealed class VALRSocketSubscriptionRequest
{
	[JsonProperty("type")]
	public VALRSocketCommands Type { get; init; } =
		VALRSocketCommands.Subscribe;

	[JsonProperty("subscriptions")]
	public VALRSocketSubscription[] Subscriptions { get; init; }
}

sealed class VALRSocketPingRequest
{
	[JsonProperty("type")]
	public VALRSocketCommands Type { get; init; } = VALRSocketCommands.Ping;
}

sealed class VALRSocketHeader
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes? Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("clientMsgId")]
	public string ClientMessageId { get; init; }
}

sealed class VALRSocketMarketSummary
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("data")]
	public VALRMarketSummary Data { get; init; }
}

sealed class VALRSocketOrderBook
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("data")]
	public VALROrderBook Data { get; init; }
}

sealed class VALRSocketTrade
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("data")]
	public VALRPublicTrade Data { get; init; }
}

sealed class VALRSocketCandle
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("data")]
	public VALRCandle Data { get; init; }
}

sealed class VALRSocketBalance
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALRSocketBalanceData Data { get; init; }
}

sealed class VALRSocketCurrency
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }
}

sealed class VALRSocketBalanceData
{
	[JsonProperty("currency")]
	public VALRSocketCurrency Currency { get; init; }

	[JsonProperty("available")]
	public decimal Available { get; init; }

	[JsonProperty("reserved")]
	public decimal Reserved { get; init; }

	[JsonProperty("total")]
	public decimal Total { get; init; }

	[JsonProperty("updatedAt")]
	public string UpdatedAt { get; init; }

	[JsonProperty("lendReserved")]
	public decimal LendReserved { get; init; }

	[JsonProperty("borrowCollateralReserved")]
	public decimal BorrowReserved { get; init; }

	[JsonProperty("borrowedAmount")]
	public decimal BorrowedAmount { get; init; }
}

sealed class VALRSocketOpenOrders
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALROpenOrder[] Data { get; init; }
}

sealed class VALRSocketOrderStatus
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALROrderStatus Data { get; init; }
}

sealed class VALRSocketAccountTrade
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("currencyPairSymbol")]
	public string CurrencyPair { get; init; }

	[JsonProperty("data")]
	public VALRAccountTrade Data { get; init; }
}

sealed class VALRSocketPosition
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALRPosition Data { get; init; }
}

sealed class VALRSocketClosedPosition
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALRSocketClosedPositionData Data { get; init; }
}

sealed class VALRSocketClosedPositionData
{
	[JsonProperty("pair")]
	public string Pair { get; init; }

	[JsonProperty("positionId")]
	public string PositionId { get; init; }
}

sealed class VALRSocketError
{
	[JsonProperty("type")]
	public VALRSocketMessageTypes Type { get; init; }

	[JsonProperty("data")]
	public VALRSocketErrorData Data { get; init; }
}

sealed class VALRSocketErrorData
{
	[JsonProperty("message")]
	public string Message { get; init; }

	[JsonProperty("failureReason")]
	public string FailureReason { get; init; }

	[JsonProperty("orderId")]
	public string OrderId { get; init; }
}
