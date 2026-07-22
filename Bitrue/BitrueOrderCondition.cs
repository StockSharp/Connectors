namespace StockSharp.Bitrue;

/// <summary>
/// Bitrue order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitrueKey)]
public class BitrueOrderCondition : OrderCondition
{
	/// <summary>
	/// Futures leverage to set before submitting the order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public int Leverage
	{
		get => (int?)Parameters.TryGetValue(nameof(Leverage)) ?? 20;
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Futures margin mode.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public MarginModes MarginMode
	{
		get => (MarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? MarginModes.Cross;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Close an existing futures position instead of opening one.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}
}
