namespace StockSharp.Backpack;

/// <summary>
/// Backpack Exchange order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BackpackKey)]
public class BackpackOrderCondition : OrderCondition
{
	/// <summary>
	/// Reduce an existing futures position only.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>
	/// Interpret market-order volume as quote-currency quantity.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.QuoteVolumeKey,
		Description = LocalizedStrings.QuoteVolumeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool IsQuoteVolume
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsQuoteVolume)) ?? false;
		set => Parameters[nameof(IsQuoteVolume)] = value;
	}
}
