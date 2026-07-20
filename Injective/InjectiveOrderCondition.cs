namespace StockSharp.Injective;

/// <summary>Injective order condition.</summary>
[DataContract]
[Serializable]
public class InjectiveOrderCondition : OrderCondition
{
	/// <summary>Trigger price for a stop or take-profit order.</summary>
	[DataMember]
	[Display(Name = "Trigger price", Description =
		"Trigger price for a conditional Injective order.",
		GroupName = "Condition", Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Whether the condition is a take-profit condition.</summary>
	[DataMember]
	[Display(Name = "Take profit", Description =
		"Use a take-profit trigger instead of a stop trigger.",
		GroupName = "Condition", Order = 1)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	/// <summary>Whether the derivative order can only reduce a position.</summary>
	[DataMember]
	[Display(Name = "Reduce only", Description =
		"Place a derivative order with zero margin so it can only reduce a position.",
		GroupName = "Condition", Order = 2)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Requested derivative leverage.</summary>
	[DataMember]
	[Display(Name = "Leverage", Description =
		"Leverage used to calculate derivative order margin.",
		GroupName = "Condition", Order = 3)]
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
