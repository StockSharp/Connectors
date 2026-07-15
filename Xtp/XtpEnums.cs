namespace StockSharp.Xtp;

/// <summary>Transport protocol used by the XTP gateway.</summary>
public enum XtpProtocols
{
	/// <summary>TCP.</summary>
	Tcp = 1,

	/// <summary>UDP. Supported by the quote service only.</summary>
	Udp = 2,
}

/// <summary>XTP native price instruction.</summary>
public enum XtpPriceTypes
{
	/// <summary>Limit.</summary>
	Limit = 1,

	/// <summary>Best price or cancel.</summary>
	BestOrCancel = 2,

	/// <summary>Best five levels, then limit.</summary>
	Best5OrLimit = 3,

	/// <summary>Best five levels or cancel.</summary>
	Best5OrCancel = 4,

	/// <summary>All or cancel.</summary>
	AllOrCancel = 5,

	/// <summary>Same-side best price.</summary>
	ForwardBest = 6,

	/// <summary>Opposite-side best price, then limit.</summary>
	ReverseBestLimit = 7,

	/// <summary>Limit fill or kill.</summary>
	LimitOrCancel = 8,
}

/// <summary>XTP native order side.</summary>
public enum XtpOrderSides
{
	/// <summary>Buy.</summary>
	Buy = 1,

	/// <summary>Sell.</summary>
	Sell = 2,

	/// <summary>Subscribe.</summary>
	Purchase = 7,

	/// <summary>Redeem.</summary>
	Redemption = 8,

	/// <summary>Split.</summary>
	Split = 9,

	/// <summary>Merge.</summary>
	Merge = 10,

	/// <summary>Margin buy.</summary>
	MarginBuy = 21,

	/// <summary>Short sell.</summary>
	ShortSell = 22,

	/// <summary>Sell securities to repay financing.</summary>
	RepayMargin = 23,

	/// <summary>Buy securities to repay a stock loan.</summary>
	RepayStock = 24,

	/// <summary>Repay a stock loan with existing securities.</summary>
	StockRepayStock = 26,

	/// <summary>Transfer collateral in.</summary>
	CollateralIn = 28,

	/// <summary>Transfer collateral out.</summary>
	CollateralOut = 29,

	/// <summary>Combine an option strategy.</summary>
	OptionCombine = 31,

	/// <summary>Split an option strategy.</summary>
	OptionSplit = 32,
}

/// <summary>XTP position effect.</summary>
public enum XtpPositionEffects
{
	/// <summary>Not specified (cash instruments).</summary>
	None = 0,

	/// <summary>Open.</summary>
	Open = 1,

	/// <summary>Close.</summary>
	Close = 2,

	/// <summary>Close today.</summary>
	CloseToday = 4,

	/// <summary>Close yesterday.</summary>
	CloseYesterday = 5,
}

/// <summary>XTP business instruction.</summary>
public enum XtpBusinessTypes
{
	/// <summary>Cash trading.</summary>
	Cash = 0,

	/// <summary>IPO subscription.</summary>
	Ipo = 1,

	/// <summary>Repo.</summary>
	Repo = 2,

	/// <summary>ETF creation/redemption.</summary>
	Etf = 3,

	/// <summary>Margin trading.</summary>
	Margin = 4,

	/// <summary>Rights issue.</summary>
	Allotment = 6,

	/// <summary>Options.</summary>
	Option = 10,
}
