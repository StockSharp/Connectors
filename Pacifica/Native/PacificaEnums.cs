namespace StockSharp.Pacifica.Native;

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaWebSocketMethods
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "ping")]
	Ping,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaSources
{
	[EnumMember(Value = "prices")]
	Prices,

	[EnumMember(Value = "bbo")]
	BestBidOffer,

	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "trades")]
	Trades,

	[EnumMember(Value = "candle")]
	Candle,

	[EnumMember(Value = "account_info")]
	AccountInfo,

	[EnumMember(Value = "account_positions")]
	AccountPositions,

	[EnumMember(Value = "account_order_updates")]
	AccountOrderUpdates,

	[EnumMember(Value = "account_trades")]
	AccountTrades,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaChannels
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "pong")]
	Pong,

	[EnumMember(Value = "prices")]
	Prices,

	[EnumMember(Value = "bbo")]
	BestBidOffer,

	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "trades")]
	Trades,

	[EnumMember(Value = "candle")]
	Candle,

	[EnumMember(Value = "account_info")]
	AccountInfo,

	[EnumMember(Value = "account_positions")]
	AccountPositions,

	[EnumMember(Value = "account_order_updates")]
	AccountOrderUpdates,

	[EnumMember(Value = "account_trades")]
	AccountTrades,

	[EnumMember(Value = "error")]
	Error,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaCandleIntervals
{
	[EnumMember(Value = "1m")]
	OneMinute,

	[EnumMember(Value = "3m")]
	ThreeMinutes,

	[EnumMember(Value = "5m")]
	FiveMinutes,

	[EnumMember(Value = "15m")]
	FifteenMinutes,

	[EnumMember(Value = "30m")]
	ThirtyMinutes,

	[EnumMember(Value = "1h")]
	OneHour,

	[EnumMember(Value = "2h")]
	TwoHours,

	[EnumMember(Value = "4h")]
	FourHours,

	[EnumMember(Value = "8h")]
	EightHours,

	[EnumMember(Value = "12h")]
	TwelveHours,

	[EnumMember(Value = "1d")]
	OneDay,

	[EnumMember(Value = "1w")]
	OneWeek,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaSides
{
	[EnumMember(Value = "bid")]
	Bid,

	[EnumMember(Value = "ask")]
	Ask,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaTradeSides
{
	[EnumMember(Value = "open_long")]
	OpenLong,

	[EnumMember(Value = "open_short")]
	OpenShort,

	[EnumMember(Value = "close_long")]
	CloseLong,

	[EnumMember(Value = "close_short")]
	CloseShort,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "ALO")]
	AddLiquidityOnly,

	[EnumMember(Value = "TOB")]
	TopOfBook,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaOrderStatuses
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "cancelled")]
	Cancelled,

	[EnumMember(Value = "rejected")]
	Rejected,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaOrderTypes
{
	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "stop_limit")]
	StopLimit,

	[EnumMember(Value = "stop_market")]
	StopMarket,

	[EnumMember(Value = "take_profit_limit")]
	TakeProfitLimit,

	[EnumMember(Value = "stop_loss_limit")]
	StopLossLimit,

	[EnumMember(Value = "take_profit_market")]
	TakeProfitMarket,

	[EnumMember(Value = "stop_loss_market")]
	StopLossMarket,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaOrderEvents
{
	[EnumMember(Value = "make")]
	Make,

	[EnumMember(Value = "stop_created")]
	StopCreated,

	[EnumMember(Value = "fulfill_market")]
	FulfillMarket,

	[EnumMember(Value = "fulfill_limit")]
	FulfillLimit,

	[EnumMember(Value = "adjust")]
	Adjust,

	[EnumMember(Value = "stop_parent_order_filled")]
	StopParentOrderFilled,

	[EnumMember(Value = "stop_triggered")]
	StopTriggered,

	[EnumMember(Value = "stop_upgrade")]
	StopUpgrade,

	[EnumMember(Value = "cancel")]
	Cancel,

	[EnumMember(Value = "force_cancel")]
	ForceCancel,

	[EnumMember(Value = "expired")]
	Expired,

	[EnumMember(Value = "post_only_rejected")]
	PostOnlyRejected,

	[EnumMember(Value = "self_trade_prevented")]
	SelfTradePrevented,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaTradeEvents
{
	[EnumMember(Value = "fulfill_maker")]
	FulfillMaker,

	[EnumMember(Value = "fulfill_taker")]
	FulfillTaker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaTradeCauses
{
	[EnumMember(Value = "normal")]
	Normal,

	[EnumMember(Value = "market_liquidation")]
	MarketLiquidation,

	[EnumMember(Value = "backstop_liquidation")]
	BackstopLiquidation,

	[EnumMember(Value = "settlement")]
	Settlement,

	[EnumMember(Value = "insolvency_liquidation")]
	InsolvencyLiquidation,

	[EnumMember(Value = "game_settlement")]
	GameSettlement,

	[EnumMember(Value = "fulfill_rfq")]
	FulfillRequestForQuote,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaInstrumentTypes
{
	[EnumMember(Value = "perpetual")]
	Perpetual,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PacificaOperationTypes
{
	[EnumMember(Value = "create_order")]
	CreateOrder,

	[EnumMember(Value = "create_market_order")]
	CreateMarketOrder,

	[EnumMember(Value = "edit_order")]
	EditOrder,

	[EnumMember(Value = "cancel_order")]
	CancelOrder,

	[EnumMember(Value = "cancel_all_orders")]
	CancelAllOrders,
}
