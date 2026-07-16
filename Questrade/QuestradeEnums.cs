namespace StockSharp.Questrade;

/// <summary>Questrade native order durations.</summary>
public enum QuestradeOrderDurations
{
	/// <summary>Day.</summary>
	Day,

	/// <summary>Good till cancelled.</summary>
	GoodTillCanceled,

	/// <summary>Good through the extended session.</summary>
	GoodTillExtendedDay,

	/// <summary>Good till date.</summary>
	GoodTillDate,

	/// <summary>Immediate or cancel.</summary>
	ImmediateOrCancel,

	/// <summary>Fill or kill.</summary>
	FillOrKill,
}

/// <summary>Questrade native order sides.</summary>
public enum QuestradeOrderSides
{
	/// <summary>Buy.</summary>
	Buy,

	/// <summary>Sell.</summary>
	Sell,

	/// <summary>Sell short.</summary>
	Short,

	/// <summary>Cover a short position.</summary>
	Cover,

	/// <summary>Buy to open an option position.</summary>
	BuyToOpen,

	/// <summary>Sell to close an option position.</summary>
	SellToClose,

	/// <summary>Sell to open an option position.</summary>
	SellToOpen,

	/// <summary>Buy to close an option position.</summary>
	BuyToClose,
}
