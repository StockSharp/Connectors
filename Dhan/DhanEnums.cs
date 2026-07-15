namespace StockSharp.Dhan;

/// <summary>Dhan order products.</summary>
public enum DhanProducts
{
	/// <summary>Cash and carry.</summary>
	Delivery,

	/// <summary>Intraday.</summary>
	Intraday,

	/// <summary>Carry-forward margin.</summary>
	Margin,

	/// <summary>Margin trading facility.</summary>
	MarginTradingFacility,

	/// <summary>Cover order.</summary>
	Cover,

	/// <summary>Bracket order.</summary>
	Bracket,
}

/// <summary>Dhan after-market execution times.</summary>
public enum DhanAfterMarketTimes
{
	/// <summary>Pre-open session.</summary>
	PreOpen,

	/// <summary>Market open.</summary>
	Open,

	/// <summary>Thirty minutes after market open.</summary>
	Open30,

	/// <summary>Sixty minutes after market open.</summary>
	Open60,
}

/// <summary>Dhan order legs.</summary>
public enum DhanOrderLegs
{
	/// <summary>Entry leg.</summary>
	Entry,

	/// <summary>Target leg.</summary>
	Target,

	/// <summary>Stop-loss leg.</summary>
	StopLoss,
}

/// <summary>Dhan Forever order flags.</summary>
public enum DhanForeverOrderFlags
{
	/// <summary>Single Good Till Triggered order.</summary>
	Single,

	/// <summary>One-cancels-other pair.</summary>
	OneCancelsOther,
}
