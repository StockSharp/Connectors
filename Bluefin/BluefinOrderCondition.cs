namespace StockSharp.Bluefin;

/// <summary>Bluefin self-trade prevention modes.</summary>
[DataContract]
public enum BluefinSelfTradePreventionTypes
{
	/// <summary>Use the account default.</summary>
	[EnumMember]
	[Display(
		Name = "Unspecified")]
	Unspecified,

	/// <summary>Cancel the taker order.</summary>
	[EnumMember]
	[Display(
		Name = "Cancel taker")]
	Taker,

	/// <summary>Cancel the maker order.</summary>
	[EnumMember]
	[Display(
		Name = "Cancel maker")]
	Maker,

	/// <summary>Cancel both orders.</summary>
	[EnumMember]
	[Display(
		Name = "Cancel both")]
	Both,
}

/// <summary>Bluefin-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BluefinKey)]
public class BluefinOrderCondition : OrderCondition,
	IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Whether the order can only reduce an existing position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Whether the order uses isolated margin.</summary>
	[DataMember]
	[Display(Name = "Isolated margin",
		Description = "Place the order in isolated-margin mode.",
		GroupName = "Parameters", Order = 1)]
	public bool IsIsolated
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsIsolated)) ?? false;
		set => Parameters[nameof(IsIsolated)] = value;
	}

	/// <summary>Requested leverage.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public decimal? Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>Stop or take-profit activation price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Whether the trigger is a take-profit trigger.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	/// <summary>Self-trade prevention mode.</summary>
	[DataMember]
	[Display(Name = "Self-trade prevention",
		Description = "Action taken when an order would self-trade.",
		GroupName = "Parameters", Order = 5)]
	public BluefinSelfTradePreventionTypes SelfTradePrevention
	{
		get => (BluefinSelfTradePreventionTypes?)Parameters.TryGetValue(
			nameof(SelfTradePrevention)) ??
			BluefinSelfTradePreventionTypes.Unspecified;
		set => Parameters[nameof(SelfTradePrevention)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => IsTakeProfit ? null : TriggerPrice;
		set
		{
			IsTakeProfit = false;
			TriggerPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => IsTakeProfit ? TriggerPrice : null;
		set
		{
			IsTakeProfit = true;
			TriggerPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }
	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get; set; }
	bool IStopLossOrderCondition.IsTrailing { get => false; set { } }
}
