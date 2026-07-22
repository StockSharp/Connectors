namespace StockSharp.BitFlyer;

/// <summary>
/// bitFlyer stop and trailing-stop parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitFlyerKey)]
public class BitFlyerOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Stop activation price.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Positive price distance for a trailing-stop order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TrailingDeltaKey,
		Description = LocalizedStrings.TrailingDeltaKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public int? TrailingOffset
	{
		get => (int?)Parameters.TryGetValue(nameof(TrailingOffset));
		set => Parameters[nameof(TrailingOffset)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => TriggerPrice;
		set => TriggerPrice = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => TrailingOffset is > 0;
		set
		{
			if (!value)
				TrailingOffset = null;
		}
	}
}
