namespace StockSharp.BloFin;

/// <summary>
/// BloFin margin modes.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum BloFinMarginModes
{
	/// <summary>
	/// Cross margin.
	/// </summary>
	[EnumMember(Value = "cross")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BloFinCrossMarginKey)]
	Cross,

	/// <summary>
	/// Isolated margin.
	/// </summary>
	[EnumMember(Value = "isolated")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BloFinIsolatedMarginKey)]
	Isolated,
}

/// <summary>
/// BloFin futures position sides.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum BloFinPositionSides
{
	/// <summary>
	/// One-way position mode.
	/// </summary>
	[EnumMember(Value = "net")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AllKey)]
	Net,

	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember(Value = "long")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember(Value = "short")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// BloFin order execution policies.
/// </summary>
[DataContract]
[Serializable]
public enum BloFinOrderPolicies
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
/// BloFin trigger price sources.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum BloFinTriggerPriceTypes
{
	/// <summary>
	/// Last traded price.
	/// </summary>
	[EnumMember(Value = "last")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LastTradeKey)]
	Last,

	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember(Value = "mark")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey)]
	Mark,

	/// <summary>
	/// Index price.
	/// </summary>
	[EnumMember(Value = "index")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IndexKey)]
	Index,
}

/// <summary>
/// BloFin order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BloFinKey)]
public class BloFinOrderCondition : OrderCondition
{
	/// <summary>
	/// Margin mode.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public BloFinMarginModes MarginMode
	{
		get => (BloFinMarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? BloFinMarginModes.Cross;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Position side.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public BloFinPositionSides PositionSide
	{
		get => (BloFinPositionSides?)Parameters.TryGetValue(nameof(PositionSide)) ?? BloFinPositionSides.Net;
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
	public BloFinOrderPolicies Policy
	{
		get => (BloFinOrderPolicies?)Parameters.TryGetValue(nameof(Policy)) ?? BloFinOrderPolicies.Regular;
		set => Parameters[nameof(Policy)] = value;
	}

	/// <summary>
	/// Leverage to set before the order is submitted. A null value keeps the exchange setting.
	/// </summary>
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

	/// <summary>
	/// Reduce an existing position only.
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
	/// Take-profit trigger price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public decimal? TakeProfitTriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitTriggerPrice));
		set => Parameters[nameof(TakeProfitTriggerPrice)] = value;
	}

	/// <summary>
	/// Take-profit order price. Use -1 for a market order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.PriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public decimal? TakeProfitOrderPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitOrderPrice));
		set => Parameters[nameof(TakeProfitOrderPrice)] = value;
	}

	/// <summary>
	/// Stop-loss trigger price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 7)]
	public decimal? StopLossTriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossTriggerPrice));
		set => Parameters[nameof(StopLossTriggerPrice)] = value;
	}

	/// <summary>
	/// Stop-loss order price. Use -1 for a market order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceKey,
		Description = LocalizedStrings.PriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 8)]
	public decimal? StopLossOrderPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossOrderPrice));
		set => Parameters[nameof(StopLossOrderPrice)] = value;
	}

	/// <summary>
	/// Trigger price source for attached take-profit and stop-loss orders.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 9)]
	public BloFinTriggerPriceTypes TriggerPriceType
	{
		get => (BloFinTriggerPriceTypes?)Parameters.TryGetValue(nameof(TriggerPriceType)) ?? BloFinTriggerPriceTypes.Last;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}
}
