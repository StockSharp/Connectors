namespace StockSharp.Grvt.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtInstrumentKinds
{
	[EnumMember(Value = "PERPETUAL")]
	Perpetual,
	[EnumMember(Value = "FUTURE")]
	Future,
	[EnumMember(Value = "CALL")]
	Call,
	[EnumMember(Value = "PUT")]
	Put,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtVenues
{
	[EnumMember(Value = "ORDERBOOK")]
	OrderBook,
	[EnumMember(Value = "RFQ")]
	Rfq,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtSettlementPeriods
{
	[EnumMember(Value = "PERPETUAL")]
	Perpetual,
	[EnumMember(Value = "DAILY")]
	Daily,
	[EnumMember(Value = "WEEKLY")]
	Weekly,
	[EnumMember(Value = "MONTHLY")]
	Monthly,
	[EnumMember(Value = "QUARTERLY")]
	Quarterly,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtCandlestickIntervals
{
	[EnumMember(Value = "CI_1_M")]
	Minute1,
	[EnumMember(Value = "CI_3_M")]
	Minute3,
	[EnumMember(Value = "CI_5_M")]
	Minute5,
	[EnumMember(Value = "CI_15_M")]
	Minute15,
	[EnumMember(Value = "CI_30_M")]
	Minute30,
	[EnumMember(Value = "CI_1_H")]
	Hour1,
	[EnumMember(Value = "CI_2_H")]
	Hour2,
	[EnumMember(Value = "CI_4_H")]
	Hour4,
	[EnumMember(Value = "CI_6_H")]
	Hour6,
	[EnumMember(Value = "CI_8_H")]
	Hour8,
	[EnumMember(Value = "CI_12_H")]
	Hour12,
	[EnumMember(Value = "CI_1_D")]
	Day1,
	[EnumMember(Value = "CI_3_D")]
	Day3,
	[EnumMember(Value = "CI_5_D")]
	Day5,
	[EnumMember(Value = "CI_1_W")]
	Week1,
	[EnumMember(Value = "CI_2_W")]
	Week2,
	[EnumMember(Value = "CI_3_W")]
	Week3,
	[EnumMember(Value = "CI_4_W")]
	Week4,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtCandlestickTypes
{
	[EnumMember(Value = "TRADE")]
	Trade,
	[EnumMember(Value = "MARK")]
	Mark,
	[EnumMember(Value = "INDEX")]
	Index,
	[EnumMember(Value = "MID")]
	Mid,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtTimeInForces
{
	[EnumMember(Value = "GOOD_TILL_TIME")]
	GoodTillTime = 1,
	[EnumMember(Value = "ALL_OR_NONE")]
	AllOrNone = 2,
	[EnumMember(Value = "IMMEDIATE_OR_CANCEL")]
	ImmediateOrCancel = 3,
	[EnumMember(Value = "FILL_OR_KILL")]
	FillOrKill = 4,
	[EnumMember(Value = "RETAIL_PRICE_IMPROVEMENT")]
	RetailPriceImprovement = 5,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtOrderStatuses
{
	[EnumMember(Value = "PENDING")]
	Pending,
	[EnumMember(Value = "OPEN")]
	Open,
	[EnumMember(Value = "FILLED")]
	Filled,
	[EnumMember(Value = "REJECTED")]
	Rejected,
	[EnumMember(Value = "CANCELLED")]
	Cancelled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtOrderRejectReasons
{
	[EnumMember(Value = "UNSPECIFIED")]
	Unspecified,
	[EnumMember(Value = "CLIENT_CANCEL")]
	ClientCancel,
	[EnumMember(Value = "CLIENT_BULK_CANCEL")]
	ClientBulkCancel,
	[EnumMember(Value = "CLIENT_SESSION_END")]
	ClientSessionEnd,
	[EnumMember(Value = "MARKET_CANCEL")]
	MarketCancel,
	[EnumMember(Value = "IOC_CANCEL")]
	IocCancel,
	[EnumMember(Value = "AON_CANCEL")]
	AonCancel,
	[EnumMember(Value = "FOK_CANCEL")]
	FokCancel,
	[EnumMember(Value = "EXPIRED")]
	Expired,
	[EnumMember(Value = "FAIL_POST_ONLY")]
	FailPostOnly,
	[EnumMember(Value = "FAIL_REDUCE_ONLY")]
	FailReduceOnly,
	[EnumMember(Value = "MM_PROTECTION")]
	MarketMakerProtection,
	[EnumMember(Value = "SELF_TRADE_PROTECTION")]
	SelfTradeProtection,
	[EnumMember(Value = "SELF_MATCHED_SUBACCOUNT")]
	SelfMatchedSubAccount,
	[EnumMember(Value = "OVERLAPPING_CLIENT_ORDER_ID")]
	OverlappingClientOrderId,
	[EnumMember(Value = "BELOW_MARGIN")]
	BelowMargin,
	[EnumMember(Value = "LIQUIDATION")]
	Liquidation,
	[EnumMember(Value = "INSTRUMENT_INVALID")]
	InstrumentInvalid,
	[EnumMember(Value = "INSTRUMENT_DEACTIVATED")]
	InstrumentDeactivated,
	[EnumMember(Value = "SYSTEM_FAILOVER")]
	SystemFailover,
	[EnumMember(Value = "UNAUTHORISED")]
	Unauthorised,
	[EnumMember(Value = "SESSION_KEY_EXPIRED")]
	SessionKeyExpired,
	[EnumMember(Value = "SUB_ACCOUNT_NOT_FOUND")]
	SubAccountNotFound,
	[EnumMember(Value = "NO_TRADE_PERMISSION")]
	NoTradePermission,
	[EnumMember(Value = "UNSUPPORTED_TIME_IN_FORCE")]
	UnsupportedTimeInForce,
	[EnumMember(Value = "MULTI_LEGGED_ORDER")]
	MultiLeggedOrder,
	[EnumMember(Value = "EXCEED_MAX_POSITION_SIZE")]
	ExceedMaxPositionSize,
	[EnumMember(Value = "EXCEED_MAX_SIGNATURE_EXPIRATION")]
	ExceedMaxSignatureExpiration,
	[EnumMember(Value = "MARKET_ORDER_WITH_LIMIT_PRICE")]
	MarketOrderWithLimitPrice,
	[EnumMember(Value = "CLIENT_CANCEL_ON_DISCONNECT_TRIGGERED")]
	ClientCancelOnDisconnectTriggered,
	[EnumMember(Value = "OCO_COUNTER_PART_TRIGGERED")]
	OcoCounterPartTriggered,
	[EnumMember(Value = "REDUCE_ONLY_LIMIT")]
	ReduceOnlyLimit,
	[EnumMember(Value = "CLIENT_REPLACE")]
	ClientReplace,
	[EnumMember(Value = "DERISK_MUST_BE_IOC")]
	DeriskMustBeIoc,
	[EnumMember(Value = "DERISK_MUST_BE_REDUCE_ONLY")]
	DeriskMustBeReduceOnly,
	[EnumMember(Value = "DERISK_NOT_SUPPORTED")]
	DeriskNotSupported,
	[EnumMember(Value = "INVALID_ORDER_TYPE")]
	InvalidOrderType,
	[EnumMember(Value = "CURRENCY_NOT_DEFINED")]
	CurrencyNotDefined,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtMarginTypes
{
	[EnumMember(Value = "SIMPLE_CROSS_MARGIN")]
	SimpleCrossMargin,
	[EnumMember(Value = "PORTFOLIO_CROSS_MARGIN")]
	PortfolioCrossMargin,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtTriggerTypesNative
{
	[EnumMember(Value = "UNSPECIFIED")]
	Unspecified,
	[EnumMember(Value = "TAKE_PROFIT")]
	TakeProfit,
	[EnumMember(Value = "STOP_LOSS")]
	StopLoss,
}

[JsonConverter(typeof(StringEnumConverter))]
enum GrvtTriggerPricesNative
{
	[EnumMember(Value = "UNSPECIFIED")]
	Unspecified,
	[EnumMember(Value = "INDEX")]
	Index,
	[EnumMember(Value = "LAST")]
	Last,
	[EnumMember(Value = "MID")]
	Mid,
	[EnumMember(Value = "MARK")]
	Mark,
}
