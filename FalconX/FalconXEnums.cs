namespace StockSharp.FalconX;

/// <summary>FalconX order directions.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum FalconXSides
{
	/// <summary>Buy the base token.</summary>
	[EnumMember(Value = "buy")]
	Buy,

	/// <summary>Sell the base token.</summary>
	[EnumMember(Value = "sell")]
	Sell,

	/// <summary>Request prices for both directions.</summary>
	[EnumMember(Value = "two_way")]
	TwoWay,
}

/// <summary>FalconX order types.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum FalconXOrderTypes
{
	/// <summary>Request for quote.</summary>
	[EnumMember(Value = "rfq")]
	Rfq,

	/// <summary>Market order.</summary>
	[EnumMember(Value = "market")]
	Market,

	/// <summary>Limit order.</summary>
	[EnumMember(Value = "limit")]
	Limit,

	/// <summary>Time-weighted average-price order.</summary>
	[EnumMember(Value = "twap")]
	Twap,
}

/// <summary>FalconX time-in-force values.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum FalconXTimeInForces
{
	/// <summary>Fill or kill.</summary>
	[EnumMember(Value = "fok")]
	FillOrKill,

	/// <summary>Good until canceled.</summary>
	[EnumMember(Value = "gtc")]
	GoodTillCanceled,

	/// <summary>Good until the supplied expiry.</summary>
	[EnumMember(Value = "gtx")]
	GoodTillExpiry,
}

/// <summary>FalconX order states.</summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum FalconXOrderStatuses
{
	/// <summary>Order is being created.</summary>
	[EnumMember(Value = "created")]
	Created,

	/// <summary>Order is open.</summary>
	[EnumMember(Value = "open")]
	Open,

	/// <summary>Order is temporarily held.</summary>
	[EnumMember(Value = "on_hold")]
	OnHold,

	/// <summary>Order is partially filled and active.</summary>
	[EnumMember(Value = "partially_filled")]
	PartiallyFilled,

	/// <summary>Order is completely filled.</summary>
	[EnumMember(Value = "success")]
	Success,

	/// <summary>Order is completely filled.</summary>
	[EnumMember(Value = "filled")]
	Filled,

	/// <summary>Order is canceled.</summary>
	[EnumMember(Value = "canceled")]
	Canceled,

	/// <summary>Order is expired.</summary>
	[EnumMember(Value = "expired")]
	Expired,

	/// <summary>Order is rejected.</summary>
	[EnumMember(Value = "rejected")]
	Rejected,

	/// <summary>Request failed.</summary>
	[EnumMember(Value = "failure")]
	Failure,

	/// <summary>Order partially filled before expiry.</summary>
	[EnumMember(Value = "partially_filled_and_expired")]
	PartiallyFilledAndExpired,

	/// <summary>Order partially filled before cancellation.</summary>
	[EnumMember(Value = "partially_filled_and_canceled")]
	PartiallyFilledAndCanceled,

	/// <summary>Order partially filled before rejection.</summary>
	[EnumMember(Value = "partially_filled_and_rejected")]
	PartiallyFilledAndRejected,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum FalconXSocketActions
{
	[EnumMember(Value = "auth")]
	Authenticate,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,

	[EnumMember(Value = "data_request")]
	DataRequest,

	[EnumMember(Value = "create_order_request")]
	CreateOrder,

	[EnumMember(Value = "update_order_request")]
	UpdateOrder,

	[EnumMember(Value = "cancel_order_request")]
	CancelOrder,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum FalconXSocketEvents
{
	[EnumMember(Value = "auth_response")]
	AuthenticationResponse,

	[EnumMember(Value = "subscribe_response")]
	SubscribeResponse,

	[EnumMember(Value = "unsubscribe_response")]
	UnsubscribeResponse,

	[EnumMember(Value = "data_response")]
	DataResponse,

	[EnumMember(Value = "stream")]
	Stream,

	[EnumMember(Value = "create_order_ack")]
	CreateOrderAcknowledged,

	[EnumMember(Value = "create_order_accepted")]
	CreateOrderAccepted,

	[EnumMember(Value = "create_order_rejected")]
	CreateOrderRejected,

	[EnumMember(Value = "update_order_ack")]
	UpdateOrderAcknowledged,

	[EnumMember(Value = "update_order_accepted")]
	UpdateOrderAccepted,

	[EnumMember(Value = "update_order_rejected")]
	UpdateOrderRejected,

	[EnumMember(Value = "cancel_order_ack")]
	CancelOrderAcknowledged,

	[EnumMember(Value = "cancel_order_accepted")]
	CancelOrderAccepted,

	[EnumMember(Value = "cancel_order_rejected")]
	CancelOrderRejected,

	[EnumMember(Value = "order_update")]
	OrderUpdate,

	[EnumMember(Value = "order_response")]
	OrderResponse,

	[EnumMember(Value = "order_rejected")]
	OrderRejected,

	[EnumMember(Value = "error_response")]
	ErrorResponse,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum FalconXSocketStatuses
{
	[EnumMember(Value = "success")]
	Success,

	[EnumMember(Value = "error")]
	Error,

	[EnumMember(Value = "failure")]
	Failure,
}

[DataContract]
[JsonConverter(typeof(StringEnumConverter))]
enum FalconXOrderQueryStatuses
{
	[EnumMember(Value = "open")]
	Open,

	[EnumMember(Value = "success")]
	Success,

	[EnumMember(Value = "failure")]
	Failure,
}
