namespace StockSharp.Coinstore;

/// <summary>
/// Coinstore-specific time-in-force value.
/// </summary>
public enum CoinstoreTimeInForce
{
	/// <summary>
	/// Keep the order active until filled or cancelled.
	/// </summary>
	GoodTillCanceled,

	/// <summary>
	/// Fill immediately and cancel the remaining quantity.
	/// </summary>
	ImmediateOrCancel,
}

/// <summary>
/// Coinstore order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinstoreKey)]
public class CoinstoreOrderCondition : OrderCondition
{
	/// <summary>
	/// Whether the order must only add liquidity.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey,
		Description = LocalizedStrings.PostOnlyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool PostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(PostOnly)) ?? false;
		set => Parameters[nameof(PostOnly)] = value;
	}

	/// <summary>
	/// Native time-in-force value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.TimeInForceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public CoinstoreTimeInForce TimeInForce
	{
		get => (CoinstoreTimeInForce?)
			Parameters.TryGetValue(nameof(TimeInForce)) ??
			CoinstoreTimeInForce.GoodTillCanceled;
		set => Parameters[nameof(TimeInForce)] = value;
	}
}
