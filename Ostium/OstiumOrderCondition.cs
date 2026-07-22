namespace StockSharp.Ostium;

/// <summary>Ostium-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OstiumKey)]
public class OstiumOrderCondition : OrderCondition
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
				"Ostium leverage must be positive.");
	}

	/// <summary>Optional take-profit price. Zero disables take profit.</summary>
	[DataMember]
	[Display(
		Name = "Take profit",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 1)]
	public decimal TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice)) ?? 0m;
		set => Parameters[nameof(TakeProfitPrice)] = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value));
	}

	/// <summary>Optional stop-loss price. Zero disables stop loss.</summary>
	[DataMember]
	[Display(
		Name = "Stop loss",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 2)]
	public decimal StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice)) ?? 0m;
		set => Parameters[nameof(StopLossPrice)] = value >= 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value));
	}

	/// <summary>Use a stop trigger instead of a regular limit order.</summary>
	[DataMember]
	[Display(
		Name = "Stop order",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 3)]
	public bool IsStopOrder
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsStopOrder)) ?? false;
		set => Parameters[nameof(IsStopOrder)] = value;
	}

	/// <summary>Restrict the trade to the protocol day-trading session.</summary>
	[DataMember]
	[Display(
		Name = "Day trade",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 4)]
	public bool IsDayTrade
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsDayTrade)) ?? false;
		set => Parameters[nameof(IsDayTrade)] = value;
	}

	/// <summary>Close an existing position instead of opening a trade.</summary>
	[DataMember]
	[Display(
		Name = "Close position",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 5)]
	public bool IsClosePosition
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsClosePosition)) ?? false;
		set => Parameters[nameof(IsClosePosition)] = value;
	}

	/// <summary>Per-pair Ostium position index used for closing.</summary>
	[DataMember]
	[Display(
		Name = "Position index",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public int? PositionIndex
	{
		get => (int?)Parameters.TryGetValue(nameof(PositionIndex));
		set => Parameters[nameof(PositionIndex)] = value;
	}

	/// <summary>Percentage of the position to close.</summary>
	[DataMember]
	[Display(
		Name = "Close percentage",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public decimal? ClosePercentage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ClosePercentage));
		set => Parameters[nameof(ClosePercentage)] = value is null or (> 0 and <= 100)
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Close percentage must be above zero and at most 100.");
	}
}
