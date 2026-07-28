namespace StockSharp.AscendEx;

/// <summary>
/// AscendEX stop-order trigger source.
/// </summary>
public enum AscendExStopTriggers
{
	/// <summary>
	/// Last traded market price.
	/// </summary>
	MarketPrice,

	/// <summary>
	/// Mark price.
	/// </summary>
	MarkPrice,

	/// <summary>
	/// Reference or index price.
	/// </summary>
	ReferencePrice,
}

/// <summary>
/// AscendEX order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AscendExKey)]
public class AscendExOrderCondition : OrderCondition,
	IStopLossOrderCondition
{
	/// <summary>
	/// Stop activation price.
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
	/// Whether a futures order may only reduce a position.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReducePositionKey,
		Description = LocalizedStrings.ReducePositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public bool ReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(ReduceOnly)) ??
			false;
		set => Parameters[nameof(ReduceOnly)] = value;
	}

	/// <summary>
	/// Stop trigger source.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public AscendExStopTriggers Trigger
	{
		get => (AscendExStopTriggers?)
			Parameters.TryGetValue(nameof(Trigger)) ??
			AscendExStopTriggers.MarketPrice;
		set => Parameters[nameof(Trigger)] = value;
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
