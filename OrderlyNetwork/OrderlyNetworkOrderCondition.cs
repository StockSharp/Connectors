namespace StockSharp.OrderlyNetwork;

/// <summary>Orderly Network order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OrderlyNetworkKey)]
public class OrderlyNetworkOrderCondition : OrderCondition
{
	/// <summary>Reduce an existing perpetual position only.</summary>
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

	/// <summary>Maximum visible quantity for an iceberg order.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VisibleVolumeKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public decimal? VisibleQuantity
	{
		get => (decimal?)Parameters.TryGetValue(nameof(VisibleQuantity));
		set => Parameters[nameof(VisibleQuantity)] = value;
	}

	/// <summary>Market-order slippage accepted by Orderly.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? Slippage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Slippage));
		set => Parameters[nameof(Slippage)] = value;
	}
}
