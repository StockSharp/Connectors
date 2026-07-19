namespace StockSharp.BitpandaFusion;

/// <summary>
/// Bitpanda Fusion trigger-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitpandaFusionKey)]
public class BitpandaFusionOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Trigger activation price. A null value creates a regular order.
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
