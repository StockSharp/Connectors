namespace StockSharp.Oanda;

/// <summary>
/// <see cref="Oanda"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OandaKey)]
public class OandaOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="OandaOrderCondition"/>.
	/// </summary>
	public OandaOrderCondition()
	{
	}

	/// <summary>
	/// If Market If Touched mode.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketKey,
		Description = LocalizedStrings.MarketOnTouchKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool? IsMarket
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarket));
		set => Parameters[nameof(IsMarket)] = value;
	}

	/// <summary>
	/// Minimum execution price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MinimumKey,
		Description = LocalizedStrings.MinimumKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? LowerBound
	{
		get => (decimal?)Parameters.TryGetValue(nameof(LowerBound));
		set => Parameters[nameof(LowerBound)] = value;
	}

	/// <summary>
	/// Maximum execution price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumKey,
		Description = LocalizedStrings.MaximumKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public decimal? UpperBound
	{
		get => (decimal?)Parameters.TryGetValue(nameof(UpperBound));
		set => Parameters[nameof(UpperBound)] = value;
	}

	/// <summary>
	/// Stop-loss offset.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public decimal? StopLossOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossOffset));
		set => Parameters[nameof(StopLossOffset)] = value;
	}

	/// <summary>
	/// Take-profit offset.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public decimal? TakeProfitOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitOffset));
		set => Parameters[nameof(TakeProfitOffset)] = value;
	}

	/// <summary>
	/// Offset of a trailing stop-loss.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TrailingStopLossKey,
		Description = LocalizedStrings.TrailingStopLossOffsetKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public decimal? TrailingStopLossOffset
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TrailingStopLossOffset));
		set => Parameters[nameof(TrailingStopLossOffset)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}
	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLossOffset;
		set => StopLossOffset = value;
	}
	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}
	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfitOffset;
		set => TakeProfitOffset = value;
	}
}