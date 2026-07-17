namespace StockSharp.RakutenRss;

/// <summary>Additional parameters for MARKETSPEED II RSS orders.</summary>
[DataContract]
[Serializable]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssKey)]
public sealed class RakutenRssOrderCondition : OrderCondition
{
	private RakutenRssOrderRoutes _route;
	private RakutenRssExecutionConditions _execution = RakutenRssExecutionConditions.Day;
	private RakutenRssAccountTypes _accountType;
	private RakutenRssMarginTypes _marginType = RakutenRssMarginTypes.Standard;
	private RakutenRssFillConditions _fillCondition = RakutenRssFillConditions.FillAndStore;
	private RakutenRssDerivativeTimeConditions _derivativeTime = RakutenRssDerivativeTimeConditions.Session;
	private bool _useSor;
	private DateTime? _validTill;
	private DateTime? _openDate;
	private decimal? _openPrice;

	/// <summary>Native order route.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssRouteKey,
		Description = LocalizedStrings.RakutenRssRouteDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public RakutenRssOrderRoutes Route
	{
		get => _route;
		set { _route = value; Parameters[nameof(Route)] = value; }
	}

	/// <summary>Equity execution condition.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssExecutionKey,
		Description = LocalizedStrings.RakutenRssExecutionDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public RakutenRssExecutionConditions Execution
	{
		get => _execution;
		set { _execution = value; Parameters[nameof(Execution)] = value; }
	}

	/// <summary>Native account classification.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AccountKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public RakutenRssAccountTypes AccountType
	{
		get => _accountType;
		set { _accountType = value; Parameters[nameof(AccountType)] = value; }
	}

	/// <summary>Native margin classification.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssMarginKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public RakutenRssMarginTypes MarginType
	{
		get => _marginType;
		set { _marginType = value; Parameters[nameof(MarginType)] = value; }
	}

	/// <summary>Use Smart Order Routing.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssSorKey,
		Description = LocalizedStrings.RakutenRssSorDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	public bool UseSor
	{
		get => _useSor;
		set { _useSor = value; Parameters[nameof(UseSor)] = value; }
	}

	/// <summary>Optional native validity date.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssValidTillKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 5)]
	public DateTime? ValidTill
	{
		get => _validTill;
		set { _validTill = value; Parameters[nameof(ValidTill)] = value; }
	}

	/// <summary>Derivative fill quantity condition.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeInForceKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 6)]
	public RakutenRssFillConditions FillCondition
	{
		get => _fillCondition;
		set { _fillCondition = value; Parameters[nameof(FillCondition)] = value; }
	}

	/// <summary>Derivative validity condition.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssDerivativeTimeKey,
		Description = LocalizedStrings.RakutenRssDerivativeTimeDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 7)]
	public RakutenRssDerivativeTimeConditions DerivativeTime
	{
		get => _derivativeTime;
		set { _derivativeTime = value; Parameters[nameof(DerivativeTime)] = value; }
	}

	/// <summary>Opening date required when closing a margin or derivative position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssOpenDateKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 8)]
	public DateTime? OpenDate
	{
		get => _openDate;
		set { _openDate = value; Parameters[nameof(OpenDate)] = value; }
	}

	/// <summary>Opening price required when closing a margin or derivative position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RakutenRssOpenPriceKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 9)]
	public decimal? OpenPrice
	{
		get => _openPrice;
		set { _openPrice = value; Parameters[nameof(OpenPrice)] = value; }
	}
}
