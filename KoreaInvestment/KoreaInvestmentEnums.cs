namespace StockSharp.KoreaInvestment;

/// <summary>Markets supported by Korea Investment &amp; Securities Open API.</summary>
[DataContract]
public enum KoreaInvestmentMarkets
{
	/// <summary>Korea Exchange.</summary>
	[EnumMember]
	Krx,

	/// <summary>Nextrade alternative trading system.</summary>
	[EnumMember]
	Nxt,

	/// <summary>KIS smart order routing between KRX and NXT.</summary>
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

	/// <summary>Hong Kong Stock Exchange.</summary>
	[EnumMember]
	HongKong,

	/// <summary>Shanghai Stock Exchange.</summary>
	[EnumMember]
	Shanghai,

	/// <summary>Shenzhen Stock Exchange.</summary>
	[EnumMember]
	Shenzhen,

	/// <summary>Tokyo Stock Exchange.</summary>
	[EnumMember]
	Tokyo,

	/// <summary>Hanoi Stock Exchange.</summary>
	[EnumMember]
	Hanoi,

	/// <summary>Ho Chi Minh Stock Exchange.</summary>
	[EnumMember]
	HoChiMinh,

	/// <summary>KRX derivatives day session.</summary>
	[EnumMember]
	KrxDerivatives,
}

/// <summary>KIS native order divisions.</summary>
[DataContract]
public enum KoreaInvestmentOrderDivisions
{
	/// <summary>Automatically map the StockSharp order type.</summary>
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

	/// <summary>Market-on-open order for supported US markets.</summary>
	[EnumMember]
	MarketOnOpen,

	/// <summary>Limit-on-open order for supported US markets.</summary>
	[EnumMember]
	LimitOnOpen,

	/// <summary>Market-on-close order for supported US markets.</summary>
	[EnumMember]
	MarketOnClose,

	/// <summary>Limit-on-close order for supported US markets.</summary>
	[EnumMember]
	LimitOnClose,
}

/// <summary>Time-in-force values supported by KRX derivatives.</summary>
[DataContract]
public enum KoreaInvestmentTimeInForces
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
