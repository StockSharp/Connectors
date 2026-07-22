namespace StockSharp.StandX;

/// <summary>
/// StandX margin modes.
/// </summary>
[DataContract]
[Serializable]
public enum StandXMarginModes
{
	/// <summary>Cross margin.</summary>
	[EnumMember]
	Cross,

	/// <summary>Isolated margin.</summary>
	[EnumMember]
	Isolated,
}

/// <summary>
/// StandX order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.StandXKey)]
public class StandXOrderCondition : OrderCondition
{
	/// <summary>
	/// Margin mode. When omitted, StandX uses the existing position setting.
	/// </summary>
	[DataMember]
	public StandXMarginModes? MarginMode
	{
		get => Parameters.TryGetValue(nameof(MarginMode), out var value)
			? (StandXMarginModes?)value
			: null;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Leverage. When omitted, StandX uses the existing position setting.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public int? Leverage
	{
		get => Parameters.TryGetValue(nameof(Leverage), out var value)
			? (int?)value
			: null;
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Restrict the order to reducing an existing position.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(nameof(IsReduceOnly), out var value) &&
			value is true;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>
	/// Optional take-profit trigger price.
	/// </summary>
	[DataMember]
	public decimal? TakeProfitPrice
	{
		get => Parameters.TryGetValue(nameof(TakeProfitPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>
	/// Optional stop-loss trigger price.
	/// </summary>
	[DataMember]
	public decimal? StopLossPrice
	{
		get => Parameters.TryGetValue(nameof(StopLossPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(StopLossPrice)] = value;
	}
}
