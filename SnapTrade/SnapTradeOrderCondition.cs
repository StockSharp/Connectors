namespace StockSharp.SnapTrade;

/// <summary>Additional parameters for SnapTrade equity orders.</summary>
[DataContract]
[Serializable]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeKey)]
public sealed class SnapTradeOrderCondition : OrderCondition
{
	private decimal? _stopPrice;
	private decimal? _notionalValue;
	private bool _isExtendedHours;
	private bool _isGoodTillCanceled;

	/// <summary>Stop trigger price for stop and stop-limit orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public decimal? StopPrice
	{
		get => _stopPrice;
		set
		{
			_stopPrice = value;
			Parameters[nameof(StopPrice)] = value;
		}
	}

	/// <summary>Cash amount for a fractional market order, mutually exclusive with unit volume.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeNotionalValueKey,
		Description = LocalizedStrings.SnapTradeNotionalValueDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public decimal? NotionalValue
	{
		get => _notionalValue;
		set
		{
			_notionalValue = value;
			Parameters[nameof(NotionalValue)] = value;
		}
	}

	/// <summary>Route a supported limit order to the extended-hours session.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeExtendedHoursKey,
		Description = LocalizedStrings.SnapTradeExtendedHoursDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public bool IsExtendedHours
	{
		get => _isExtendedHours;
		set
		{
			_isExtendedHours = value;
			Parameters[nameof(IsExtendedHours)] = value;
		}
	}

	/// <summary>Use GTC instead of the default day duration.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SnapTradeGoodTillCanceledKey,
		Description = LocalizedStrings.SnapTradeGoodTillCanceledDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public bool IsGoodTillCanceled
	{
		get => _isGoodTillCanceled;
		set
		{
			_isGoodTillCanceled = value;
			Parameters[nameof(IsGoodTillCanceled)] = value;
		}
	}
}
