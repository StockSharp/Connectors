namespace StockSharp.Bitunix;

/// <summary>
/// Bitunix order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitunixKey)]
public class BitunixOrderCondition : OrderCondition
{
	/// <summary>
	/// Futures leverage.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey, GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public int Leverage
	{
		get => (int?)Parameters.TryGetValue(nameof(Leverage)) ?? 20;
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Futures margin mode.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey, GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public MarginModes MarginMode
	{
		get => (MarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? MarginModes.Cross;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Reduce an existing futures position only.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}
}
