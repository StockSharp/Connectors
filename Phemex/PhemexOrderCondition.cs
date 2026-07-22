namespace StockSharp.Phemex;

/// <summary>
/// Phemex futures position sides.
/// </summary>
[DataContract]
[Serializable]
public enum PhemexPositionSides
{
	/// <summary>
	/// One-way mode.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AllKey)]
	Both,

	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// Phemex order execution policies.
/// </summary>
[DataContract]
[Serializable]
public enum PhemexOrderPolicies
{
	/// <summary>
	/// Regular limit or market order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RegularKey)]
	Regular,

	/// <summary>
	/// Immediate-or-cancel.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ImmediateOrCancelKey)]
	ImmediateOrCancel,

	/// <summary>
	/// Fill-or-kill.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FillOrKillKey)]
	FillOrKill,

	/// <summary>
	/// Post-only.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey)]
	PostOnly,
}

/// <summary>
/// Phemex order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PhemexKey)]
public class PhemexOrderCondition : OrderCondition
{
	/// <summary>
	/// Quote-currency amount for a spot market buy.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey, GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}

	/// <summary>
	/// Futures position side.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey, GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public PhemexPositionSides PositionSide
	{
		get => (PhemexPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? PhemexPositionSides.Both;
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Order execution policy.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public PhemexOrderPolicies Policy
	{
		get => (PhemexOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? PhemexOrderPolicies.Regular;
		set => Parameters[nameof(Policy)] = value;
	}

	/// <summary>
	/// Reduce an existing futures position only.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}
}
