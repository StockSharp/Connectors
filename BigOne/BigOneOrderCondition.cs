namespace StockSharp.BigOne;

/// <summary>
/// BigONE spot and contract order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BigOneKey)]
public class BigOneOrderCondition : OrderCondition,
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
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Whether the order must only add liquidity.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey,
		Description = LocalizedStrings.PostOnlyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool PostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(PostOnly)) ?? false;
		set => Parameters[nameof(PostOnly)] = value;
	}

	/// <summary>
	/// Whether the trigger activates when the market reaches or exceeds
	/// <see cref="StopPrice"/>.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DirectionKey,
		Description = LocalizedStrings.DirectionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public bool TriggerAbove
	{
		get => (bool?)Parameters.TryGetValue(nameof(TriggerAbove)) ??
			false;
		set => Parameters[nameof(TriggerAbove)] = value;
	}

	/// <summary>
	/// Whether a contract order may only reduce an existing position.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClosePositionKey,
		Description = LocalizedStrings.ClosePositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public bool ReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(ReduceOnly)) ??
			false;
		set => Parameters[nameof(ReduceOnly)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
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
