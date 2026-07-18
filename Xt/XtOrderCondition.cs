namespace StockSharp.Xt;

/// <summary>
/// XT.COM futures position sides.
/// </summary>
[DataContract]
[Serializable]
public enum XtPositionSides
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
/// XT.COM order execution policies.
/// </summary>
[DataContract]
[Serializable]
public enum XtOrderPolicies
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
/// XT.COM order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.XtKey)]
public class XtOrderCondition : OrderCondition
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
	public XtPositionSides PositionSide
	{
		get => (XtPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? XtPositionSides.Both;
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Order execution policy.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public XtOrderPolicies Policy
	{
		get => (XtOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? XtOrderPolicies.Regular;
		set => Parameters[nameof(Policy)] = value;
	}
}
