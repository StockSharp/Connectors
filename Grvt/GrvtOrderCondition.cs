namespace StockSharp.Grvt;

/// <summary>
/// GRVT trigger order types.
/// </summary>
[DataContract]
[Serializable]
public enum GrvtTriggerTypes
{
	/// <summary>
	/// A regular order without a trigger.
	/// </summary>
	[EnumMember]
	None,

	/// <summary>
	/// Take-profit trigger.
	/// </summary>
	[EnumMember]
	TakeProfit,

	/// <summary>
	/// Stop-loss trigger.
	/// </summary>
	[EnumMember]
	StopLoss,
}

/// <summary>
/// GRVT trigger price sources.
/// </summary>
[DataContract]
[Serializable]
public enum GrvtTriggerPrices
{
	/// <summary>Index price.</summary>
	[EnumMember]
	Index,
	/// <summary>Last trade price.</summary>
	[EnumMember]
	Last,
	/// <summary>Order-book midpoint.</summary>
	[EnumMember]
	Mid,
	/// <summary>Mark price.</summary>
	[EnumMember]
	Mark,
}

/// <summary>
/// GRVT order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GrvtKey)]
public class GrvtOrderCondition : OrderCondition
{
	/// <summary>
	/// Trigger order type.
	/// </summary>
	[DataMember]
	public GrvtTriggerTypes TriggerType
	{
		get => (GrvtTriggerTypes?)Parameters.TryGetValue(nameof(TriggerType)) ??
			GrvtTriggerTypes.None;
		set => Parameters[nameof(TriggerType)] = value;
	}

	/// <summary>
	/// Price source used to activate the trigger.
	/// </summary>
	[DataMember]
	public GrvtTriggerPrices TriggerBy
	{
		get => (GrvtTriggerPrices?)Parameters.TryGetValue(nameof(TriggerBy)) ??
			GrvtTriggerPrices.Mark;
		set => Parameters[nameof(TriggerBy)] = value;
	}

	/// <summary>
	/// Trigger activation price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? ActivationPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ActivationPrice));
		set => Parameters[nameof(ActivationPrice)] = value;
	}

	/// <summary>
	/// Execute the triggered order as a market order.
	/// </summary>
	[DataMember]
	public bool IsMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarket)) ?? false;
		set => Parameters[nameof(IsMarket)] = value;
	}

	/// <summary>
	/// Close the entire position when triggered.
	/// </summary>
	[DataMember]
	public bool IsClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ?? false;
		set => Parameters[nameof(IsClosePosition)] = value;
	}

	/// <summary>
	/// Restrict the order to reducing an existing position.
	/// </summary>
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
}
