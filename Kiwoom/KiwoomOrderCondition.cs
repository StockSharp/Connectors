namespace StockSharp.Kiwoom;

/// <summary>Additional parameters for Kiwoom orders.</summary>
[DataContract]
[Serializable]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomOrderConditionKey)]
public sealed class KiwoomOrderCondition : OrderCondition
{
	private KiwoomMarkets? _market;
	private KiwoomOrderDivisions _division;
	private KiwoomTimeInForces _timeInForce;
	private decimal? _stopPrice;

	/// <summary>Explicit market. When omitted, the connector derives it from the security board.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomMarketKey,
		Description = LocalizedStrings.KiwoomMarketDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public KiwoomMarkets? Market
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
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomOrderDivisionKey,
		Description = LocalizedStrings.KiwoomOrderDivisionDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public KiwoomOrderDivisions Division
	{
		get => _division;
		set
		{
			_division = value;
			Parameters[nameof(Division)] = value;
		}
	}

	/// <summary>Time in force for domestic orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.KiwoomTimeInForceDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public KiwoomTimeInForces TimeInForce
	{
		get => _timeInForce;
		set
		{
			_timeInForce = value;
			Parameters[nameof(TimeInForce)] = value;
		}
	}

	/// <summary>Stop price for stop and stop-limit orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KiwoomStopPriceKey,
		Description = LocalizedStrings.KiwoomStopPriceDescKey, GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public decimal? StopPrice
	{
		get => _stopPrice;
		set
		{
			_stopPrice = value;
			Parameters[nameof(StopPrice)] = value;
		}
	}
}
