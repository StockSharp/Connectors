namespace StockSharp.SunIo;

/// <summary>SUN.io-specific swap parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SunIoKey)]
public class SunIoOrderCondition : OrderCondition
{
	/// <summary>Optional per-order slippage tolerance in basis points.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public int? SlippageToleranceBasisPoints
	{
		get => (int?)Parameters.TryGetValue(
			nameof(SlippageToleranceBasisPoints));
		set => Parameters[nameof(SlippageToleranceBasisPoints)] = value;
	}

	/// <summary>Optional per-order transaction deadline interval.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public TimeSpan? DeadlineInterval
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(DeadlineInterval));
		set => Parameters[nameof(DeadlineInterval)] = value;
	}
}
