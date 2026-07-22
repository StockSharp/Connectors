namespace StockSharp.Reya;

using Native;

/// <summary>Reya order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ReyaKey)]
public class ReyaOrderCondition : OrderCondition
{
	/// <summary>Restrict the order to reducing an existing position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Conditional-order trigger price.</summary>
	[DataMember]
	[Display(Name = "Trigger price",
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Conditional-order trigger kind.</summary>
	[DataMember]
	[Display(Name = "Trigger type",
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public ReyaTriggerOrderTypes TriggerType
	{
		get => Parameters.TryGetValue(nameof(TriggerType), out var value)
			? (ReyaTriggerOrderTypes)value
			: ReyaTriggerOrderTypes.StopLoss;
		set => Parameters[nameof(TriggerType)] = value;
	}
}
