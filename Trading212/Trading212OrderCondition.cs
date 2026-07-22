namespace StockSharp.Trading212;

/// <summary>Additional parameters for Trading 212 orders.</summary>
[DataContract]
[Serializable]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.Trading212Key)]
public sealed class Trading212OrderCondition : OrderCondition
{
	private decimal? _stopPrice;
	private bool _isExtendedHours;

	/// <summary>Trigger price for stop and stop-limit orders.</summary>
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

	/// <summary>Whether a market order may execute during supported extended hours.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.Trading212ExtendedHoursKey,
		Description = LocalizedStrings.Trading212ExtendedHoursDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public bool IsExtendedHours
	{
		get => _isExtendedHours;
		set
		{
			_isExtendedHours = value;
			Parameters[nameof(IsExtendedHours)] = value;
		}
	}
}
