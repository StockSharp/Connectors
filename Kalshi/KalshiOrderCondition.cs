namespace StockSharp.Kalshi;

/// <summary>Kalshi-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KalshiKey)]
public class KalshiOrderCondition : OrderCondition
{
	/// <summary>Whether the order may only add liquidity.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PostOnlyKey,
		Description = LocalizedStrings.PostOnlyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool IsPostOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsPostOnly)) ?? false;
		set => Parameters[nameof(IsPostOnly)] = value;
	}

	/// <summary>Whether the order may only reduce an existing position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Whether Kalshi cancels the order when exchange trading is paused.</summary>
	[DataMember]
	[Display(
		Name = "Cancel on pause",
		Description = "Cancel a resting order when Kalshi pauses trading.",
		GroupName = "Parameters",
		Order = 2)]
	public bool IsCancelOnPause
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsCancelOnPause)) ?? false;
		set => Parameters[nameof(IsCancelOnPause)] = value;
	}

	/// <summary>Self-trade prevention mode.</summary>
	[DataMember]
	[Display(
		Name = "Self-trade prevention",
		Description = "Action taken when the order would trade with another order from the same account.",
		GroupName = "Parameters",
		Order = 3)]
	public KalshiSelfTradePreventionTypes SelfTradePreventionType
	{
		get => (KalshiSelfTradePreventionTypes?)Parameters.TryGetValue(
			nameof(SelfTradePreventionType)) ??
			KalshiSelfTradePreventionTypes.TakerAtCross;
		set => Parameters[nameof(SelfTradePreventionType)] = value;
	}

	/// <summary>Optional Kalshi order-group identifier.</summary>
	[DataMember]
	[Display(
		Name = "Order group",
		Description = "Optional Kalshi order-group identifier.",
		GroupName = "Parameters",
		Order = 4)]
	public string OrderGroupId
	{
		get => (string)Parameters.TryGetValue(nameof(OrderGroupId));
		set => Parameters[nameof(OrderGroupId)] = value;
	}
}
