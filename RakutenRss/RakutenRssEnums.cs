namespace StockSharp.RakutenRss;

/// <summary>Native MARKETSPEED II RSS order route.</summary>
[DataContract]
public enum RakutenRssOrderRoutes
{
	/// <summary>Cash equity order.</summary>
	[EnumMember]
	Cash,

	/// <summary>Margin position opening order.</summary>
	[EnumMember]
	MarginOpen,

	/// <summary>Margin position closing order.</summary>
	[EnumMember]
	MarginClose,

	/// <summary>Futures or options position opening order.</summary>
	[EnumMember]
	DerivativeOpen,

	/// <summary>Futures or options position closing order.</summary>
	[EnumMember]
	DerivativeClose,
}

/// <summary>Native execution condition for equity orders.</summary>
[DataContract]
public enum RakutenRssExecutionConditions
{
	/// <summary>Valid for the current day.</summary>
	[EnumMember]
	Day = 1,

	/// <summary>Valid for the current week.</summary>
	[EnumMember]
	Week = 2,

	/// <summary>Execute at the opening.</summary>
	[EnumMember]
	Opening = 3,

	/// <summary>Execute at the close.</summary>
	[EnumMember]
	Closing = 4,

	/// <summary>Valid through a specified date.</summary>
	[EnumMember]
	GoodTillDate = 5,

	/// <summary>Market-on-close if not filled.</summary>
	[EnumMember]
	MarketOnClose = 6,

	/// <summary>Fill at market if not filled.</summary>
	[EnumMember]
	MarketIfTouched = 7,
}

/// <summary>Native account classification.</summary>
[DataContract]
public enum RakutenRssAccountTypes
{
	/// <summary>Specified account.</summary>
	[EnumMember]
	Specified = 0,

	/// <summary>General account.</summary>
	[EnumMember]
	General = 1,

	/// <summary>NISA account.</summary>
	[EnumMember]
	Nisa = 2,

	/// <summary>Legacy NISA account.</summary>
	[EnumMember]
	LegacyNisa = 3,
}

/// <summary>Native margin classification.</summary>
[DataContract]
public enum RakutenRssMarginTypes
{
	/// <summary>Standard six-month margin.</summary>
	[EnumMember]
	Standard = 1,

	/// <summary>General unlimited margin.</summary>
	[EnumMember]
	Unlimited = 2,

	/// <summary>General fourteen-day margin.</summary>
	[EnumMember]
	FourteenDays = 3,

	/// <summary>General one-day margin.</summary>
	[EnumMember]
	OneDay = 4,
}

/// <summary>Derivative fill quantity condition.</summary>
[DataContract]
public enum RakutenRssFillConditions
{
	/// <summary>Fill or kill.</summary>
	[EnumMember]
	FillOrKill = 1,

	/// <summary>Fill and kill.</summary>
	[EnumMember]
	FillAndKill = 2,

	/// <summary>Fill and store.</summary>
	[EnumMember]
	FillAndStore = 3,
}

/// <summary>Derivative time condition.</summary>
[DataContract]
public enum RakutenRssDerivativeTimeConditions
{
	/// <summary>Current session.</summary>
	[EnumMember]
	Session = 1,

	/// <summary>Execute at the close.</summary>
	[EnumMember]
	Closing = 4,

	/// <summary>Valid through a specified date.</summary>
	[EnumMember]
	GoodTillDate = 5,

	/// <summary>Valid through the final trading day.</summary>
	[EnumMember]
	FinalTradingDay = 9,
}
