namespace StockSharp.WooX;

/// <summary>
/// WOO X margin modes.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum WooXMarginModes
{
	/// <summary>
	/// Cross margin.
	/// </summary>
	[EnumMember(Value = "CROSS")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WooXCrossMarginKey)]
	Cross,

	/// <summary>
	/// Isolated margin.
	/// </summary>
	[EnumMember(Value = "ISOLATED")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WooXIsolatedMarginKey)]
	Isolated,
}

/// <summary>
/// WOO X futures position sides.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum WooXPositionSides
{
	/// <summary>
	/// One-way position.
	/// </summary>
	[EnumMember(Value = "BOTH")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AllKey)]
	Both,

	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember(Value = "LONG")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember(Value = "SHORT")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// WOO X order execution policies.
/// </summary>
[DataContract]
[Serializable]
public enum WooXOrderPolicies
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
/// WOO X order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WooXKey)]
public class WooXOrderCondition : OrderCondition
{
	/// <summary>
	/// Futures margin mode.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public WooXMarginModes MarginMode
	{
		get => (WooXMarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? WooXMarginModes.Cross;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Futures position side. Use <see cref="WooXPositionSides.Both"/> for one-way mode.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public WooXPositionSides PositionSide
	{
		get => (WooXPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? WooXPositionSides.Both;
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Order execution policy.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderTypeKey,
		Description = LocalizedStrings.OrderTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public WooXOrderPolicies Policy
	{
		get => (WooXOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? WooXOrderPolicies.Regular;
		set => Parameters[nameof(Policy)] = value;
	}

	/// <summary>
	/// Leverage to set before a perpetual order. A null value preserves the exchange setting.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public int? Leverage
	{
		get => (int?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Reduce an existing perpetual position only.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>
	/// Interpret market-order volume as quote-currency amount.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public bool IsQuoteVolume
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsQuoteVolume)) ?? false;
		set => Parameters[nameof(IsQuoteVolume)] = value;
	}
}
