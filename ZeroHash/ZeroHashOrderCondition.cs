namespace StockSharp.ZeroHash;

/// <summary>Zero Hash-specific CLOB order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ZeroHashKey)]
public sealed class ZeroHashOrderCondition : OrderCondition
{
	/// <summary>Optional stop trigger price.</summary>
	[DataMember]
	[Display(Name = "Trigger price",
		Description = "Stop trigger price for Zero Hash stop and stop-limit orders.",
		GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Stop trigger source.</summary>
	[DataMember]
	[Display(Name = "Trigger method",
		Description = "Price source used to trigger a Zero Hash stop order.",
		GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public ZeroHashTriggerMethods TriggerMethod
	{
		get => (ZeroHashTriggerMethods?)Parameters.TryGetValue(nameof(TriggerMethod)) ??
			ZeroHashTriggerMethods.LastPrice;
		set => Parameters[nameof(TriggerMethod)] = value;
	}

	/// <summary>Self-match prevention instruction.</summary>
	[DataMember]
	[Display(Name = "Self-match prevention",
		Description = "Action taken when the order would self-match.",
		GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public ZeroHashSelfMatchPreventionInstructions SelfMatchPreventionInstruction
	{
		get => (ZeroHashSelfMatchPreventionInstructions?)Parameters.TryGetValue(
			nameof(SelfMatchPreventionInstruction)) ??
			ZeroHashSelfMatchPreventionInstructions.Undefined;
		set => Parameters[nameof(SelfMatchPreventionInstruction)] = value;
	}

	/// <summary>Regulatory order capacity.</summary>
	[DataMember]
	[Display(Name = "Order capacity",
		Description = "Regulatory capacity in which the order is submitted.",
		GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public ZeroHashOrderCapacities OrderCapacity
	{
		get => (ZeroHashOrderCapacities?)Parameters.TryGetValue(nameof(OrderCapacity)) ??
			ZeroHashOrderCapacities.Undefined;
		set => Parameters[nameof(OrderCapacity)] = value;
	}

	/// <summary>Require the entire quantity to execute.</summary>
	[DataMember]
	[Display(Name = "All or none",
		Description = "Require the complete quantity to execute.",
		GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public bool IsAllOrNone
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsAllOrNone)) ?? false;
		set => Parameters[nameof(IsAllOrNone)] = value;
	}

	/// <summary>Use the best same-side limit.</summary>
	[DataMember]
	[Display(Name = "Best limit",
		Description = "Enter the order at the current best same-side price.",
		GroupName = LocalizedStrings.ParametersKey, Order = 5)]
	public bool IsBestLimit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsBestLimit)) ?? false;
		set => Parameters[nameof(IsBestLimit)] = value;
	}

	/// <summary>Require strict limit-price handling.</summary>
	[DataMember]
	[Display(Name = "Strict limit",
		Description = "Require execution at the supplied limit price.",
		GroupName = LocalizedStrings.ParametersKey, Order = 6)]
	public bool IsStrictLimit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsStrictLimit)) ?? false;
		set => Parameters[nameof(IsStrictLimit)] = value;
	}

	/// <summary>Bypass exchange price-validity checks when entitled.</summary>
	[DataMember]
	[Display(Name = "Ignore price checks",
		Description = "Request bypass of price-validity checks when permitted.",
		GroupName = LocalizedStrings.ParametersKey, Order = 7)]
	public bool IsIgnorePriceValidityChecks
	{
		get => (bool?)Parameters.TryGetValue(
			nameof(IsIgnorePriceValidityChecks)) ?? false;
		set => Parameters[nameof(IsIgnorePriceValidityChecks)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new ZeroHashOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
