namespace StockSharp.Longbridge;

/// <summary>Native Longbridge order types.</summary>
public enum LongbridgeOrderTypes
{
	/// <summary>Limit order.</summary>
	Limit,

	/// <summary>Enhanced limit order.</summary>
	EnhancedLimit,

	/// <summary>Market order.</summary>
	Market,

	/// <summary>At-auction order.</summary>
	AtAuction,

	/// <summary>At-auction limit order.</summary>
	AtAuctionLimit,

	/// <summary>Odd-lot order.</summary>
	OddLot,

	/// <summary>Limit-if-touched order.</summary>
	LimitIfTouched,

	/// <summary>Market-if-touched order.</summary>
	MarketIfTouched,

	/// <summary>Trailing limit order by amount.</summary>
	TrailingLimitAmount,

	/// <summary>Trailing limit order by percentage.</summary>
	TrailingLimitPercent,

	/// <summary>Trailing market order by amount.</summary>
	TrailingMarketAmount,

	/// <summary>Trailing market order by percentage.</summary>
	TrailingMarketPercent,
}

/// <summary>Longbridge regular-hours policies.</summary>
public enum LongbridgeOutsideRths
{
	/// <summary>Regular trading hours only.</summary>
	RegularOnly,

	/// <summary>Allow every supported session.</summary>
	AnyTime,

	/// <summary>Overnight session.</summary>
	Overnight,

	/// <summary>Option pre-market session.</summary>
	OptionPreMarket,
}

/// <summary>Longbridge native time-in-force policies.</summary>
public enum LongbridgeTimeInForces
{
	/// <summary>Day order.</summary>
	Day,

	/// <summary>Good till canceled.</summary>
	GoodTillCanceled,

	/// <summary>Good till date.</summary>
	GoodTillDate,
}
