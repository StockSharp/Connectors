namespace StockSharp.Kiwoom;

/// <summary>Markets supported by the Kiwoom REST API.</summary>
[DataContract]
public enum KiwoomMarkets
{
	/// <summary>Korea Exchange.</summary>
	[EnumMember]
	Krx,

	/// <summary>Nextrade alternative trading system.</summary>
	[EnumMember]
	Nxt,

	/// <summary>Kiwoom smart order routing between KRX and NXT.</summary>
	[EnumMember]
	Sor,

	/// <summary>NASDAQ.</summary>
	[EnumMember]
	Nasdaq,

	/// <summary>New York Stock Exchange.</summary>
	[EnumMember]
	Nyse,

	/// <summary>NYSE American.</summary>
	[EnumMember]
	Amex,
}

/// <summary>Kiwoom native order divisions.</summary>
[DataContract]
public enum KiwoomOrderDivisions
{
	/// <summary>Derive the native division from the StockSharp order type.</summary>
	[EnumMember]
	Auto,

	/// <summary>Limit order.</summary>
	[EnumMember]
	Limit,

	/// <summary>Market order.</summary>
	[EnumMember]
	Market,

	/// <summary>Conditional limit order.</summary>
	[EnumMember]
	ConditionalLimit,

	/// <summary>Best-price order.</summary>
	[EnumMember]
	Best,

	/// <summary>Priority-price order.</summary>
	[EnumMember]
	Priority,

	/// <summary>Before-open order.</summary>
	[EnumMember]
	BeforeOpen,

	/// <summary>After-close order.</summary>
	[EnumMember]
	AfterClose,

	/// <summary>After-hours single-price order.</summary>
	[EnumMember]
	AfterHoursSingle,

	/// <summary>Midpoint order.</summary>
	[EnumMember]
	Midpoint,

	/// <summary>Stop-limit order.</summary>
	[EnumMember]
	StopLimit,

	/// <summary>Limit-on-close order.</summary>
	[EnumMember]
	LimitOnClose,

	/// <summary>Market-on-close order.</summary>
	[EnumMember]
	MarketOnClose,

	/// <summary>Stop-market order.</summary>
	[EnumMember]
	Stop,

	/// <summary>VWAP limit order.</summary>
	[EnumMember]
	VwapLimit,

	/// <summary>TWAP limit order.</summary>
	[EnumMember]
	TwapLimit,

	/// <summary>VWAP market order.</summary>
	[EnumMember]
	VwapMarket,

	/// <summary>TWAP market order.</summary>
	[EnumMember]
	TwapMarket,
}

/// <summary>Kiwoom domestic time-in-force values.</summary>
[DataContract]
public enum KiwoomTimeInForces
{
	/// <summary>Day order.</summary>
	[EnumMember]
	Day,

	/// <summary>Immediate or cancel.</summary>
	[EnumMember]
	Ioc,

	/// <summary>Fill or kill.</summary>
	[EnumMember]
	Fok,
}
