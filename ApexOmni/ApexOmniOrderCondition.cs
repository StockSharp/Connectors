namespace StockSharp.ApexOmni;

/// <summary>
/// ApeX Omni conditional order kinds.
/// </summary>
[DataContract]
[Serializable]
public enum ApexOmniTriggerTypes
{
	/// <summary>Regular order without a trigger.</summary>
	[EnumMember]
	None,

	/// <summary>Stop-loss order.</summary>
	[EnumMember]
	StopLoss,

	/// <summary>Take-profit order.</summary>
	[EnumMember]
	TakeProfit,
}

/// <summary>
/// ApeX Omni trigger price sources.
/// </summary>
[DataContract]
[Serializable]
public enum ApexOmniTriggerPrices
{
	/// <summary>Market price.</summary>
	[EnumMember]
	Market,

	/// <summary>Index price.</summary>
	[EnumMember]
	Index,

	/// <summary>Oracle price.</summary>
	[EnumMember]
	Oracle,
}

/// <summary>
/// ApeX Omni order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ApexOmniKey)]
public class ApexOmniOrderCondition : OrderCondition
{
	/// <summary>
	/// Conditional order kind.
	/// </summary>
	[DataMember]
	public ApexOmniTriggerTypes TriggerType
	{
		get => (ApexOmniTriggerTypes?)Parameters.TryGetValue(
			nameof(TriggerType)) ?? ApexOmniTriggerTypes.None;
		set => Parameters[nameof(TriggerType)] = value;
	}

	/// <summary>
	/// Trigger price source.
	/// </summary>
	[DataMember]
	public ApexOmniTriggerPrices TriggerPrice
	{
		get => (ApexOmniTriggerPrices?)Parameters.TryGetValue(
			nameof(TriggerPrice)) ?? ApexOmniTriggerPrices.Market;
		set => Parameters[nameof(TriggerPrice)] = value;
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
