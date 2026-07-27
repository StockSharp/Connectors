namespace StockSharp.Injective;

/// <summary>Injective order condition.</summary>
[DataContract]
[Serializable]
public class InjectiveOrderCondition : OrderCondition
{
	/// <summary>Trigger price for a stop or take-profit order.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerPriceKey,
		Description = LocalizedStrings.TriggerPriceForAConditionalInjectiveOrderDescKey,
		GroupName = LocalizedStrings.ConditionKey,
		Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Whether the condition is a take-profit condition.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitLabelKey,
		Description = LocalizedStrings.UseATakeProfitTriggerInsteadOfAStopTriggerDescKey,
		GroupName = LocalizedStrings.ConditionKey,
		Order = 1)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	/// <summary>Whether the derivative order can only reduce a position.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ReduceOnlyKey,
		Description = LocalizedStrings.PlaceADerivativeOrderWithZeroMarginSoItCanOnlyReduceAPositionDescKey,
		GroupName = LocalizedStrings.ConditionKey,
		Order = 2)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Requested derivative leverage.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageUsedToCalculateDerivativeOrderMarginDescKey,
		GroupName = LocalizedStrings.ConditionKey,
		Order = 3)]
	public decimal? Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
		=> new InjectiveOrderCondition
		{
			TriggerPrice = TriggerPrice,
			IsTakeProfit = IsTakeProfit,
			IsReduceOnly = IsReduceOnly,
			Leverage = Leverage,
		};
}
