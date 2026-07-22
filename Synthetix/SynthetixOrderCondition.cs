namespace StockSharp.Synthetix;

/// <summary>Synthetix-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SynthetixKey)]
public class SynthetixOrderCondition : OrderCondition
{
	/// <summary>Trigger price for stop-loss or take-profit orders.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value is null or > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Synthetix trigger price must be positive.");
	}

	/// <summary>Whether a conditional order is a take-profit order.</summary>
	[DataMember]
	[Display(Name = "Take profit", Description =
		"Use the take-profit trigger instead of stop-loss.",
		GroupName = LocalizedStrings.TransactionKey, Order = 1)]
	public bool IsTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTakeProfit)) ?? false;
		set => Parameters[nameof(IsTakeProfit)] = value;
	}

	/// <summary>Whether a trigger executes as a market order.</summary>
	[DataMember]
	[Display(Name = "Trigger market", Description =
		"Execute the conditional order at market when triggered.",
		GroupName = LocalizedStrings.TransactionKey, Order = 2)]
	public bool IsTriggerMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTriggerMarket)) ?? true;
		set => Parameters[nameof(IsTriggerMarket)] = value;
	}

	/// <summary>Whether the order may only reduce an existing position.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.TransactionKey, Order = 3)]
	public bool IsReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsReduceOnly)) ?? false;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Whether a trigger closes the entire position.</summary>
	[DataMember]
	[Display(Name = "Close position", Description =
		"Close the entire position when a conditional order triggers.",
		GroupName = LocalizedStrings.TransactionKey, Order = 4)]
	public bool IsClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ?? false;
		set => Parameters[nameof(IsClosePosition)] = value;
	}

	/// <summary>Trigger evaluation price.</summary>
	[DataMember]
	[Display(Name = "Trigger price type", Description =
		"Price used to evaluate the trigger.",
		GroupName = LocalizedStrings.TransactionKey, Order = 5)]
	public SynthetixTriggerPriceTypes TriggerPriceType
	{
		get => (SynthetixTriggerPriceTypes?)Parameters.TryGetValue(
			nameof(TriggerPriceType)) ?? SynthetixTriggerPriceTypes.Mark;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}
}

/// <summary>Synthetix trigger price types.</summary>
[DataContract]
public enum SynthetixTriggerPriceTypes
{
	/// <summary>Mark price.</summary>
	[EnumMember]
	[Display(
		Name = "Mark")]
	Mark,

	/// <summary>Last traded price.</summary>
	[EnumMember]
	[Display(
		Name = "Last")]
	Last,
}
