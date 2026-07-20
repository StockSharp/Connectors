namespace StockSharp.Extended;

using Native;

/// <summary>Extended order condition.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ExtendedKey)]
public class ExtendedOrderCondition : OrderCondition
{
	/// <summary>Restrict the order to reducing an existing position.</summary>
	[DataMember]
	public bool IsReduceOnly
	{
		get => Parameters.TryGetValue(nameof(IsReduceOnly), out var value) &&
			value is true;
		set => Parameters[nameof(IsReduceOnly)] = value;
	}

	/// <summary>Optional conditional-order trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(nameof(TriggerPrice), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Price source used by a conditional order.</summary>
	[DataMember]
	public ExtendedTriggerPriceTypes TriggerPriceType
	{
		get => Parameters.TryGetValue(nameof(TriggerPriceType), out var value)
			? (ExtendedTriggerPriceTypes)value
			: ExtendedTriggerPriceTypes.Mark;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}

	/// <summary>Direction in which the trigger price must be crossed.</summary>
	[DataMember]
	public ExtendedTriggerDirections TriggerDirection
	{
		get => Parameters.TryGetValue(nameof(TriggerDirection), out var value)
			? (ExtendedTriggerDirections)value
			: ExtendedTriggerDirections.Up;
		set => Parameters[nameof(TriggerDirection)] = value;
	}

	/// <summary>Execution price mode after a conditional order triggers.</summary>
	[DataMember]
	public ExtendedExecutionPriceTypes ExecutionPriceType
	{
		get => Parameters.TryGetValue(nameof(ExecutionPriceType), out var value)
			? (ExtendedExecutionPriceTypes)value
			: ExtendedExecutionPriceTypes.Limit;
		set => Parameters[nameof(ExecutionPriceType)] = value;
	}

	/// <summary>Optional taker fee override expressed as a decimal rate.</summary>
	[DataMember]
	public decimal? TakerFee
	{
		get => Parameters.TryGetValue(nameof(TakerFee), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(TakerFee)] = value;
	}

	/// <summary>Optional builder fee expressed as a decimal rate.</summary>
	[DataMember]
	public decimal? BuilderFee
	{
		get => Parameters.TryGetValue(nameof(BuilderFee), out var value)
			? (decimal?)value
			: null;
		set => Parameters[nameof(BuilderFee)] = value;
	}

	/// <summary>Optional Extended builder identifier.</summary>
	[DataMember]
	public long? BuilderId
	{
		get => Parameters.TryGetValue(nameof(BuilderId), out var value)
			? (long?)value
			: null;
		set => Parameters[nameof(BuilderId)] = value;
	}
}
