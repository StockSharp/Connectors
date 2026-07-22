namespace StockSharp.Aevo;

/// <summary>Aevo-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AevoKey)]
public class AevoOrderCondition : OrderCondition
{
	/// <summary>Whether the order can only reduce an existing position.</summary>
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

	/// <summary>Whether market-maker protection applies to the order.</summary>
	[DataMember]
	[Display(Name = "Market-maker protection",
		Description = "Include the order in Aevo market-maker protection.",
		GroupName = "Parameters", Order = 1)]
	public bool IsMmp
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMmp)) ?? false;
		set => Parameters[nameof(IsMmp)] = value;
	}
}
