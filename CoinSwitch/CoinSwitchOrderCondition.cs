namespace StockSharp.CoinSwitch;

/// <summary>
/// CoinSwitch futures conditional-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinSwitchKey)]
public class CoinSwitchOrderCondition : OrderCondition,
	IStopLossOrderCondition
{
	/// <summary>
	/// Trigger activation price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Whether the order only reduces an existing futures position.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReducePositionKey,
		Description = LocalizedStrings.ReducePositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool? ReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(ReduceOnly));
		set => Parameters[nameof(ReduceOnly)] = value;
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
		get => false;
		set { }
	}
}
