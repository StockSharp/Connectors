namespace StockSharp.Pionex;

/// <summary>
/// Pionex futures position sides.
/// </summary>
[DataContract]
[Serializable]
public enum PionexPositionSides
{
	/// <summary>
	/// One-way mode.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllKey)]
	Both,

	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// Pionex futures order policies.
/// </summary>
[DataContract]
[Serializable]
public enum PionexOrderPolicies
{
	/// <summary>
	/// Regular limit or market order.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RegularKey)]
	Regular,

	/// <summary>
	/// Immediate-or-cancel.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ImmediateOrCancelKey)]
	ImmediateOrCancel,

	/// <summary>
	/// Fill-or-kill.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FillOrKillKey)]
	FillOrKill,

	/// <summary>
	/// Post-only.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PostOnlyKey)]
	PostOnly,
}

/// <summary>
/// Pionex order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PionexKey)]
public class PionexOrderCondition : OrderCondition
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
	public PionexPositionSides PositionSide
	{
		get => (PionexPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? PionexPositionSides.Both;
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Futures execution policy.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public PionexOrderPolicies Policy
	{
		get => (PionexOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? PionexOrderPolicies.Regular;
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
