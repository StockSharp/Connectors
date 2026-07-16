namespace StockSharp.SierraChartDtc;

internal enum DtcMessageTypes : ushort
{
	LogonRequest = 1,
	LogonResponse = 2,
	Heartbeat = 3,
	Logoff = 5,
	EncodingRequest = 6,
	EncodingResponse = 7,
	MarketDataFeedStatus = 100,
	MarketDataRequest = 101,
	MarketDepthRequest = 102,
	MarketDataReject = 103,
	MarketDataSnapshot = 104,
	MarketDepthUpdateLevel = 106,
	MarketDataUpdateTrade = 107,
	MarketDataUpdateBidAsk = 108,
	MarketDataUpdateTradeCompact = 112,
	MarketDataUpdateSessionVolume = 113,
	MarketDataUpdateSessionHigh = 114,
	MarketDataUpdateSessionLow = 115,
	MarketDataFeedSymbolStatus = 116,
	MarketDataUpdateBidAskCompact = 117,
	MarketDataUpdateSessionSettlement = 119,
	MarketDataUpdateSessionOpen = 120,
	MarketDepthReject = 121,
	MarketDepthSnapshotLevel = 122,
	MarketDataUpdateOpenInterest = 124,
	MarketDataUpdateLastTradeSnapshot = 134,
	MarketDataUpdateSessionNumTrades = 135,
	MarketDataUpdateTradingSessionDate = 136,
	MarketDataUpdateTradeWithUnbundledIndicator = 137,
	TradingSymbolStatus = 138,
	MarketDepthUpdateLevelFloatWithMilliseconds = 140,
	MarketDepthUpdateLevelNoTimestamp = 141,
	MarketDataUpdateTradeNoTimestamp = 142,
	MarketDataUpdateBidAskNoTimestamp = 143,
	MarketDataUpdateBidAskFloatWithMicroseconds = 144,
	MarketDepthSnapshotLevelFloat = 145,
	MarketDataUpdateTradeWithUnbundledIndicator2 = 146,
	MarketDataUpdateTradeV2 = 147,
	MarketDataUpdateBidAskV2 = 148,
	CancelOrder = 203,
	CancelReplaceOrder = 204,
	SubmitNewSingleOrder = 208,
	OpenOrdersRequest = 300,
	OrderUpdate = 301,
	OpenOrdersReject = 302,
	HistoricalOrderFillsRequest = 303,
	HistoricalOrderFillResponse = 304,
	CurrentPositionsRequest = 305,
	PositionUpdate = 306,
	CurrentPositionsReject = 307,
	HistoricalOrderFillsReject = 308,
	TradeAccountsRequest = 400,
	TradeAccountResponse = 401,
	SymbolsForExchangeRequest = 502,
	SecurityDefinitionForSymbolRequest = 506,
	SecurityDefinitionResponse = 507,
	SymbolSearchRequest = 508,
	SecurityDefinitionReject = 509,
	SecurityDefinitionResponseV2 = 510,
	AccountBalanceUpdate = 600,
	AccountBalanceRequest = 601,
	AccountBalanceReject = 602,
	UserMessage = 700,
	GeneralLogMessage = 701,
	AlertMessage = 702,
	HistoricalPriceDataRequest = 800,
	HistoricalPriceDataResponseHeader = 801,
	HistoricalPriceDataReject = 802,
	HistoricalPriceDataRecordResponse = 803,
	HistoricalPriceDataTickRecordResponse = 804,
	HistoricalPriceDataResponseTrailer = 807,
}

internal enum DtcEncodings
{
	Binary,
	BinaryWithVariableLengthStrings,
	Json,
	CompactJson,
	ProtocolBuffers,
}

internal enum DtcLogonStatuses
{
	Success = 1,
	Error,
	ErrorNoReconnect,
	ReconnectNewAddress,
}

[Flags]
internal enum DtcSierraLogonFlags
{
	None = 0,
	SupportUnbundledTrades = 0x4,
	UseMarketDepthUpdatesWithMilliseconds = 0x80,
	SupportMarketDepthSnapshotFloat = 0x800,
	SupportMillisecondOrderTimestamps = 0x20000,
	SupportTradeUpdatesWithMicroseconds = 0x80000,
	SupportBidAskUpdatesWithMicroseconds = 0x100000,
}

internal enum DtcMarketDataFeedStatuses
{
	Unset,
	Unavailable,
	Available,
}

internal enum DtcRequestActions
{
	Subscribe = 1,
	Unsubscribe,
	Snapshot,
	SnapshotWithIntervalUpdates,
}

internal enum DtcOrderStatuses
{
	Unspecified,
	OrderSent,
	PendingOpen,
	PendingChild,
	Open,
	PendingCancelReplace,
	PendingCancel,
	Filled,
	Canceled,
	Rejected,
	PartiallyFilled,
}

internal enum DtcOrderUpdateReasons
{
	Unset,
	OpenOrdersResponse,
	NewOrderAccepted,
	GeneralOrderUpdate,
	OrderFilled,
	OrderPartiallyFilled,
	OrderCanceled,
	CancelReplaceComplete,
	NewOrderRejected,
	CancelRejected,
	CancelReplaceRejected,
}

internal enum DtcOrderTypes
{
	Unset,
	Market,
	Limit,
	Stop,
	StopLimit,
	MarketIfTouched,
	LimitIfTouched,
	MarketLimit,
}

internal enum DtcTimeInForces
{
	Unset,
	Day,
	GoodTillCanceled,
	GoodTillDateTime,
	ImmediateOrCancel,
	AllOrNone,
	FillOrKill,
}

internal enum DtcBuySells
{
	Unset,
	Buy,
	Sell,
}

internal enum DtcOpenCloses
{
	Unset,
	Open,
	Close,
}

internal enum DtcSecurityTypes
{
	Unset,
	Futures,
	Stock,
	Forex,
	Index,
	FuturesStrategy,
	StockOption,
	FuturesOption,
	IndexOption,
	Bond,
	MutualFund,
}

internal enum DtcPutCalls : byte
{
	Unset,
	Call,
	Put,
}

internal enum DtcSearchTypes
{
	Unset,
	BySymbol,
	ByDescription,
}

internal enum DtcAtBidOrAsks : byte
{
	Unset,
	Bid,
	Ask,
}

internal enum DtcDepthUpdateTypes : byte
{
	Unset,
	InsertOrUpdate,
	Delete,
}

internal enum DtcFinalUpdates : byte
{
	Unset,
	Final,
	NotFinal,
	BeginBatch,
}

internal enum DtcTradingStatuses : byte
{
	Unknown,
	PreOpen,
	Open,
	Closed,
	Halted,
}
