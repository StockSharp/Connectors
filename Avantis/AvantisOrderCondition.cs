namespace StockSharp.Avantis;

/// <summary>Avantis-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AvantisKey)]
public class AvantisOrderCondition : OrderCondition
{
	/// <summary>Position leverage.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 0)]
	public decimal Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage)) ?? 10m;
		set => Parameters[nameof(Leverage)] = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Avantis leverage must be positive.");
	}

	/// <summary>Required take-profit price for a newly opened trade.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitLabelKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 1)]
	public decimal TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice)) ?? 0m;
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>Optional stop-loss price. Zero disables stop loss.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossLabelKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 2)]
	public decimal StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice)) ?? 0m;
		set => Parameters[nameof(StopLossPrice)] = value;
	}

	/// <summary>Use Avantis zero-fee perpetual execution.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ZeroFeePerpetualKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 3)]
	public bool IsZeroFee
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsZeroFee)) ?? false;
		set => Parameters[nameof(IsZeroFee)] = value;
	}

	/// <summary>Use a stop-limit trigger instead of a regular limit order.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLimitLabelKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 4)]
	public bool IsStopLimit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsStopLimit)) ?? false;
		set => Parameters[nameof(IsStopLimit)] = value;
	}

	/// <summary>
	/// Close an existing position instead of opening a new trade.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClosePositionKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 5)]
	public bool IsClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ?? false;
		set => Parameters[nameof(IsClosePosition)] = value;
	}

	/// <summary>Per-pair Avantis position index used for closing.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionIndexKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public int? PositionIndex
	{
		get => (int?)Parameters.TryGetValue(nameof(PositionIndex));
		set => Parameters[nameof(PositionIndex)] = value;
	}

	/// <summary>
	/// Optional ETH execution fee override. The adapter default is used when
	/// omitted.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExecutionFeeKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public decimal? ExecutionFee
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ExecutionFee));
		set => Parameters[nameof(ExecutionFee)] = value;
	}
}
