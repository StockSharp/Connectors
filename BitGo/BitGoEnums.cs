namespace StockSharp.BitGo;

/// <summary>BitGo Prime funding source.</summary>
[DataContract]
public enum BitGoFundingTypes
{
	/// <summary>Fund the order from the Go account balance.</summary>
	[EnumMember(Value = "funded")]
	Funded,

	/// <summary>Fund the order from the margin account.</summary>
	[EnumMember(Value = "margin")]
	Margin,
}

/// <summary>BitGo Prime order type.</summary>
[DataContract]
public enum BitGoOrderTypes
{
	/// <summary>Market order.</summary>
	[EnumMember(Value = "market")]
	Market,

	/// <summary>Limit order.</summary>
	[EnumMember(Value = "limit")]
	Limit,

	/// <summary>Time-weighted average price order.</summary>
	[EnumMember(Value = "twap")]
	Twap,

	/// <summary>Steady Pace algorithmic order.</summary>
	[EnumMember(Value = "steady_pace")]
	SteadyPace,

	/// <summary>Stop or stop-limit order.</summary>
	[EnumMember(Value = "stop")]
	Stop,
}

/// <summary>BitGo Prime order status.</summary>
[DataContract]
public enum BitGoOrderStatuses
{
	/// <summary>Pending acceptance.</summary>
	[EnumMember(Value = "pending_open")]
	PendingOpen,

	/// <summary>Open.</summary>
	[EnumMember(Value = "open")]
	Open,

	/// <summary>Completed.</summary>
	[EnumMember(Value = "completed")]
	Completed,

	/// <summary>Cancellation is pending.</summary>
	[EnumMember(Value = "pending_cancel")]
	PendingCancel,

	/// <summary>Canceled.</summary>
	[EnumMember(Value = "canceled")]
	Canceled,

	/// <summary>Failed.</summary>
	[EnumMember(Value = "error")]
	Error,

	/// <summary>Scheduled for later execution.</summary>
	[EnumMember(Value = "scheduled")]
	Scheduled,
}

/// <summary>BitGo order side.</summary>
[DataContract]
public enum BitGoSides
{
	/// <summary>Buy.</summary>
	[EnumMember(Value = "buy")]
	Buy,

	/// <summary>Sell.</summary>
	[EnumMember(Value = "sell")]
	Sell,
}

/// <summary>BitGo time-in-force value.</summary>
[DataContract]
public enum BitGoTimeInForces
{
	/// <summary>Good till canceled.</summary>
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	/// <summary>Fill or kill.</summary>
	[EnumMember(Value = "FOK")]
	FillOrKill,

	/// <summary>Good till date.</summary>
	[EnumMember(Value = "GTD")]
	GoodTillDate,
}

/// <summary>TWAP progression bounds.</summary>
[DataContract]
public enum BitGoBoundsControls
{
	/// <summary>Narrow bounds.</summary>
	[EnumMember(Value = "narrow")]
	Narrow,

	/// <summary>Standard bounds.</summary>
	[EnumMember(Value = "standard")]
	Standard,

	/// <summary>Wide bounds.</summary>
	[EnumMember(Value = "wide")]
	Wide,
}

/// <summary>Steady Pace interval unit.</summary>
[DataContract]
public enum BitGoIntervalUnits
{
	/// <summary>Seconds.</summary>
	[EnumMember(Value = "second")]
	Second,

	/// <summary>Minutes.</summary>
	[EnumMember(Value = "minute")]
	Minute,

	/// <summary>Hours.</summary>
	[EnumMember(Value = "hour")]
	Hour,
}

/// <summary>BitGo WebSocket command.</summary>
[DataContract]
public enum BitGoSocketActions
{
	/// <summary>Subscribe.</summary>
	[EnumMember(Value = "subscribe")]
	Subscribe,

	/// <summary>Unsubscribe.</summary>
	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

/// <summary>BitGo WebSocket channel.</summary>
[DataContract]
public enum BitGoSocketChannels
{
	/// <summary>Level 2 order book.</summary>
	[EnumMember(Value = "level2")]
	Level2,

	/// <summary>Private order events.</summary>
	[EnumMember(Value = "orders")]
	Orders,
}

/// <summary>BitGo WebSocket message discriminator.</summary>
[DataContract]
public enum BitGoSocketMessageTypes
{
	/// <summary>Subscription acknowledgement.</summary>
	[EnumMember(Value = "subscription_response")]
	SubscriptionResponse,

	/// <summary>Order-book snapshot.</summary>
	[EnumMember(Value = "snapshot")]
	Snapshot,

	/// <summary>Incremental order-book update.</summary>
	[EnumMember(Value = "update")]
	Update,

	/// <summary>Market order update.</summary>
	[EnumMember(Value = "market")]
	Market,

	/// <summary>Limit order update.</summary>
	[EnumMember(Value = "limit")]
	Limit,

	/// <summary>TWAP order update.</summary>
	[EnumMember(Value = "twap")]
	Twap,

	/// <summary>Steady Pace order update.</summary>
	[EnumMember(Value = "steady_pace")]
	SteadyPace,

	/// <summary>Stop order update.</summary>
	[EnumMember(Value = "stop")]
	Stop,
}

/// <summary>BitGo subscription result.</summary>
[DataContract]
public enum BitGoSubscriptionStatuses
{
	/// <summary>Subscribed.</summary>
	[EnumMember(Value = "subscribed")]
	Subscribed,

	/// <summary>The subscription already existed.</summary>
	[EnumMember(Value = "already_subscribed")]
	AlreadySubscribed,

	/// <summary>Unsubscribed.</summary>
	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,
}

/// <summary>FIX ExecType value published by BitGo.</summary>
[DataContract]
public enum BitGoExecutionTypes
{
	/// <summary>New.</summary>
	[EnumMember(Value = "0")]
	New,

	/// <summary>Trade fill.</summary>
	[EnumMember(Value = "F")]
	Trade,

	/// <summary>Pending new.</summary>
	[EnumMember(Value = "A")]
	PendingNew,

	/// <summary>Pending cancel.</summary>
	[EnumMember(Value = "6")]
	PendingCancel,

	/// <summary>Canceled.</summary>
	[EnumMember(Value = "4")]
	Canceled,

	/// <summary>Rejected.</summary>
	[EnumMember(Value = "8")]
	Rejected,

	/// <summary>Order status summary.</summary>
	[EnumMember(Value = "I")]
	OrderStatus,
}

/// <summary>BitGo cancellation reason.</summary>
[DataContract]
public enum BitGoOrderReasons
{
	/// <summary>Internal processing error.</summary>
	[EnumMember(Value = "internalError")]
	InternalError,

	/// <summary>Insufficient funds.</summary>
	[EnumMember(Value = "insufficientFunds")]
	InsufficientFunds,
}
