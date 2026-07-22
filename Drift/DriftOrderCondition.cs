namespace StockSharp.Drift;

/// <summary>Drift-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DriftKey)]
public class DriftOrderCondition : OrderCondition
{
	/// <summary>Position margin mode.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public DriftMarginModes MarginMode
	{
		get => (DriftMarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ??
			DriftMarginModes.Cross;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>Whether the order can only reduce an existing position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Whether the order must only add liquidity.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey,
		Description = LocalizedStrings.PostOnlyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public bool IsPostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsPostOnly)) ?? false;
		set => Parameters[nameof(IsPostOnly)] = value;
	}

	/// <summary>Optional maximum position leverage.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public decimal? Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}
}
