namespace StockSharp.KabuStation;

/// <summary>kabu Station exchange codes.</summary>
public enum KabuStationExchanges
{
	/// <summary>Tokyo Stock Exchange.</summary>
	Tokyo = 1,
	/// <summary>Osaka derivatives, day and night sessions.</summary>
	OsakaAll = 2,
	/// <summary>Nagoya Stock Exchange.</summary>
	Nagoya = 3,
	/// <summary>Fukuoka Stock Exchange.</summary>
	Fukuoka = 5,
	/// <summary>Sapporo Securities Exchange.</summary>
	Sapporo = 6,
	/// <summary>Smart order routing.</summary>
	Sor = 9,
	/// <summary>Osaka derivatives day session.</summary>
	OsakaDay = 23,
	/// <summary>Osaka derivatives night session.</summary>
	OsakaNight = 24,
	/// <summary>Tokyo Stock Exchange Plus route.</summary>
	TokyoPlus = 27,
}

/// <summary>kabu Station account types.</summary>
public enum KabuStationAccountTypes
{
	/// <summary>General account.</summary>
	General = 2,
	/// <summary>Specified account.</summary>
	Specified = 4,
	/// <summary>Corporate account.</summary>
	Corporate = 12,
}

/// <summary>Cash and margin transaction modes.</summary>
public enum KabuStationCashMargins
{
	/// <summary>Cash transaction.</summary>
	Cash = 1,
	/// <summary>Open a margin position.</summary>
	MarginOpen = 2,
	/// <summary>Close a margin position.</summary>
	MarginClose = 3,
}

/// <summary>Margin transaction types.</summary>
public enum KabuStationMarginTradeTypes
{
	/// <summary>Standardized margin.</summary>
	Standard = 1,
	/// <summary>General long-term margin.</summary>
	GeneralLongTerm = 2,
	/// <summary>General day-trade margin.</summary>
	GeneralDayTrade = 3,
}

/// <summary>Derivative trade modes.</summary>
public enum KabuStationDerivativeTradeTypes
{
	/// <summary>Open a position.</summary>
	Open = 1,
	/// <summary>Close a position.</summary>
	Close = 2,
}

/// <summary>Native derivative time-in-force values.</summary>
public enum KabuStationTimeInForces
{
	/// <summary>Fill and store.</summary>
	Fas = 1,
	/// <summary>Fill and kill.</summary>
	Fak = 2,
	/// <summary>Fill or kill.</summary>
	Fok = 3,
}

/// <summary>Stop trigger comparisons.</summary>
public enum KabuStationTriggerComparisons
{
	/// <summary>Trigger when the price is at or below the stop.</summary>
	AtOrBelow = 1,
	/// <summary>Trigger when the price is at or above the stop.</summary>
	AtOrAbove = 2,
}

/// <summary>Post-trigger order types.</summary>
public enum KabuStationAfterHitOrderTypes
{
	/// <summary>Market.</summary>
	Market = 1,
	/// <summary>Limit.</summary>
	Limit = 2,
	/// <summary>Limit, then market at the session close. Stocks only.</summary>
	LimitThenMarket = 3,
}
