namespace StockSharp.BitoPro;

/// <summary>
/// BitoPro stop-limit order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitoProKey)]
public class BitoProOrderCondition : OrderCondition,
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
	/// Whether the stop activates when the market price is greater than
	/// or equal to <see cref="TriggerPrice"/>.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConditionKey,
		Description = LocalizedStrings.ConditionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool TriggerOnGreaterOrEqual
	{
		get => (bool?)Parameters.TryGetValue(
			nameof(TriggerOnGreaterOrEqual)) ?? true;
		set => Parameters[nameof(TriggerOnGreaterOrEqual)] = value;
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
