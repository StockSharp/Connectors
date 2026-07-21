namespace StockSharp.ZeroHash;

/// <summary>Zero Hash order directions.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashSides
{
	/// <summary>Buy.</summary>
	[EnumMember(Value = "SIDE_BUY")]
	Buy,

	/// <summary>Sell.</summary>
	[EnumMember(Value = "SIDE_SELL")]
	Sell,
}

/// <summary>Zero Hash CLOB order types.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashOrderTypes
{
	/// <summary>Market order converted to a limit order for any remainder.</summary>
	[EnumMember(Value = "ORDER_TYPE_MARKET_TO_LIMIT")]
	MarketToLimit,

	/// <summary>Limit order.</summary>
	[EnumMember(Value = "ORDER_TYPE_LIMIT")]
	Limit,

	/// <summary>Stop market order.</summary>
	[EnumMember(Value = "ORDER_TYPE_STOP")]
	Stop,

	/// <summary>Stop limit order.</summary>
	[EnumMember(Value = "ORDER_TYPE_STOP_LIMIT")]
	StopLimit,
}

/// <summary>Zero Hash time-in-force values.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashTimeInForces
{
	/// <summary>Good till canceled.</summary>
	[EnumMember(Value = "TIME_IN_FORCE_GOOD_TILL_CANCEL")]
	GoodTillCanceled,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember(Value = "TIME_IN_FORCE_IMMEDIATE_OR_CANCEL")]
	ImmediateOrCancel,

	/// <summary>Fill or kill.</summary>
	[EnumMember(Value = "TIME_IN_FORCE_FILL_OR_KILL")]
	FillOrKill,

	/// <summary>Good till the supplied UTC time.</summary>
	[EnumMember(Value = "TIME_IN_FORCE_GOOD_TILL_TIME")]
	GoodTillTime,
}

/// <summary>Zero Hash order states.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashOrderStates
{
	/// <summary>New active order.</summary>
	[EnumMember(Value = "ORDER_STATE_NEW")]
	New,

	/// <summary>Partially filled active order.</summary>
	[EnumMember(Value = "ORDER_STATE_PARTIALLY_FILLED")]
	PartiallyFilled,

	/// <summary>Filled order.</summary>
	[EnumMember(Value = "ORDER_STATE_FILLED")]
	Filled,

	/// <summary>Canceled order.</summary>
	[EnumMember(Value = "ORDER_STATE_CANCELED")]
	Canceled,

	/// <summary>Replaced order.</summary>
	[EnumMember(Value = "ORDER_STATE_REPLACED")]
	Replaced,

	/// <summary>Rejected order.</summary>
	[EnumMember(Value = "ORDER_STATE_REJECTED")]
	Rejected,

	/// <summary>Expired order.</summary>
	[EnumMember(Value = "ORDER_STATE_EXPIRED")]
	Expired,

	/// <summary>Pending new.</summary>
	[EnumMember(Value = "ORDER_STATE_PENDING_NEW")]
	PendingNew,

	/// <summary>Pending replacement.</summary>
	[EnumMember(Value = "ORDER_STATE_PENDING_REPLACE")]
	PendingReplace,

	/// <summary>Pending cancellation.</summary>
	[EnumMember(Value = "ORDER_STATE_PENDING_CANCEL")]
	PendingCancel,

	/// <summary>Pending risk approval.</summary>
	[EnumMember(Value = "ORDER_STATE_PENDING_RISK")]
	PendingRisk,
}

/// <summary>Zero Hash instrument states.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashInstrumentStates
{
	/// <summary>Closed.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_CLOSED")]
	Closed,

	/// <summary>Open.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_OPEN")]
	Open,

	/// <summary>Pre-open.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_PREOPEN")]
	PreOpen,

	/// <summary>Suspended.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_SUSPENDED")]
	Suspended,

	/// <summary>Expired.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_EXPIRED")]
	Expired,

	/// <summary>Terminated.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_TERMINATED")]
	Terminated,

	/// <summary>Halted.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_HALTED")]
	Halted,

	/// <summary>Match-and-close auction.</summary>
	[EnumMember(Value = "INSTRUMENT_STATE_MATCH_AND_CLOSE_AUCTION")]
	MatchAndCloseAuction,
}

/// <summary>Zero Hash execution event types.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashExecutionTypes
{
	/// <summary>New order.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_NEW")]
	New,

	/// <summary>Partial fill.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_PARTIAL_FILL")]
	PartialFill,

	/// <summary>Complete fill.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_FILL")]
	Fill,

	/// <summary>Cancellation.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_CANCELED")]
	Canceled,

	/// <summary>Replacement.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_REPLACE")]
	Replace,

	/// <summary>Rejection.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_REJECTED")]
	Rejected,

	/// <summary>Expiry.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_EXPIRED")]
	Expired,

	/// <summary>Done for day.</summary>
	[EnumMember(Value = "EXECUTION_TYPE_DONE_FOR_DAY")]
	DoneForDay,
}

/// <summary>Zero Hash order rejection reasons.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashOrderRejectReasons
{
	/// <summary>Exchange option.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_EXCHANGE_OPTION")]
	ExchangeOption,

	/// <summary>Unknown symbol.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_UNKNOWN_SYMBOL")]
	UnknownSymbol,

	/// <summary>Exchange closed.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_EXCHANGE_CLOSED")]
	ExchangeClosed,

	/// <summary>Incorrect quantity.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_INCORRECT_QUANTITY")]
	IncorrectQuantity,

	/// <summary>Invalid price increment.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_INVALID_PRICE_INCREMENT")]
	InvalidPriceIncrement,

	/// <summary>Incorrect order type.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_INCORRECT_ORDER_TYPE")]
	IncorrectOrderType,

	/// <summary>Price outside exchange limits.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_PRICE_OUT_OF_BOUNDS")]
	PriceOutOfBounds,

	/// <summary>No available liquidity.</summary>
	[EnumMember(Value = "ORD_REJECT_REASON_NO_LIQUIDITY")]
	NoLiquidity,

	/// <summary>Unspecified.</summary>
	[EnumMember(Value = "ORDER_REJECT_REASON_UNDEFINED")]
	Undefined,
}

/// <summary>Zero Hash self-match prevention instructions.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashSelfMatchPreventionInstructions
{
	/// <summary>Use exchange default.</summary>
	[EnumMember(Value = "SELF_MATCH_PREVENTION_INSTRUCTION_UNDEFINED")]
	Undefined,

	/// <summary>Reject the incoming order.</summary>
	[EnumMember(Value = "SELF_MATCH_PREVENTION_INSTRUCTION_REJECT_AGGRESSOR")]
	RejectAggressor,

	/// <summary>Cancel resting orders.</summary>
	[EnumMember(Value = "SELF_MATCH_PREVENTION_INSTRUCTION_CANCEL_RESTING")]
	CancelResting,

	/// <summary>Remove both sides.</summary>
	[EnumMember(Value = "SELF_MATCH_PREVENTION_INSTRUCTION_REMOVE_BOTH")]
	RemoveBoth,
}

/// <summary>Zero Hash regulatory order capacities.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashOrderCapacities
{
	/// <summary>Unspecified.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_UNDEFINED")]
	Undefined,

	/// <summary>Agency.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_AGENCY")]
	Agency,

	/// <summary>Principal.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_PRINCIPAL")]
	Principal,

	/// <summary>Proprietary.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_PROPRIETARY")]
	Proprietary,

	/// <summary>Individual.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_INDIVIDUAL")]
	Individual,

	/// <summary>Riskless principal.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_RISKLESS_PRINCIPAL")]
	RisklessPrincipal,

	/// <summary>Agent for another member.</summary>
	[EnumMember(Value = "ORDER_CAPACITY_AGENT_FOR_OTHER_MEMBER")]
	AgentForOtherMember,
}

/// <summary>Zero Hash stop trigger methods.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum ZeroHashTriggerMethods
{
	/// <summary>Use exchange default.</summary>
	[EnumMember(Value = "CONDITION_TRIGGER_METHOD_UNDEFINED")]
	Undefined,

	/// <summary>Last traded price.</summary>
	[EnumMember(Value = "CONDITION_TRIGGER_METHOD_LAST_PRICE")]
	LastPrice,

	/// <summary>Settlement price.</summary>
	[EnumMember(Value = "CONDITION_TRIGGER_METHOD_SETTLEMENT_PRICE")]
	SettlementPrice,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum ZeroHashManualOrderIndicators
{
	[EnumMember(Value = "MANUAL_ORDER_INDICATOR_MANUAL")]
	Manual,

	[EnumMember(Value = "MANUAL_ORDER_INDICATOR_AUTOMATED")]
	Automated,

	[EnumMember(Value = "MANUAL_ORDER_INDICATOR_UNDEFINED")]
	Undefined,
}
