namespace StockSharp.KoreaInvestment;

/// <summary>Additional parameters for KIS orders.</summary>
[DataContract]
[Serializable]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KoreaInvestmentOrderConditionKey)]
public sealed class KoreaInvestmentOrderCondition : OrderCondition
{
	private KoreaInvestmentMarkets? _market;
	private KoreaInvestmentOrderDivisions _division;
	private KoreaInvestmentTimeInForces _timeInForce;
	private bool _isNight;

	/// <summary>Explicit KIS market. When omitted, the connector derives it from the security board.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentMarketKey,
		Description = LocalizedStrings.KoreaInvestmentMarketDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public KoreaInvestmentMarkets? Market
	{
		get => _market;
		set
		{
			_market = value;
			Parameters[nameof(Market)] = value;
		}
	}

	/// <summary>Native order division.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentOrderDivisionKey,
		Description = LocalizedStrings.KoreaInvestmentOrderDivisionDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public KoreaInvestmentOrderDivisions Division
	{
		get => _division;
		set
		{
			_division = value;
			Parameters[nameof(Division)] = value;
		}
	}

	/// <summary>Time in force.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.KoreaInvestmentTimeInForceDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public KoreaInvestmentTimeInForces TimeInForce
	{
		get => _timeInForce;
		set
		{
			_timeInForce = value;
			Parameters[nameof(TimeInForce)] = value;
		}
	}

	/// <summary>Use the KRX derivatives night session.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KoreaInvestmentNightSessionKey,
		Description = LocalizedStrings.KoreaInvestmentNightSessionDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public bool IsNight
	{
		get => _isNight;
		set
		{
			_isNight = value;
			Parameters[nameof(IsNight)] = value;
		}
	}
}
