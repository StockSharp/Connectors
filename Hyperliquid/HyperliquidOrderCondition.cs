namespace StockSharp.Hyperliquid;

/// <summary>
/// Hyperliquid trigger order type.
/// </summary>
[DataContract]
public enum HyperliquidOrderConditionTypes
{
	/// <summary>
	/// Stop-loss.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey)]
	[EnumMember]
	StopLoss,

	/// <summary>
	/// Take-profit.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,
}

/// <summary>
/// <see cref="Hyperliquid"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.HyperliquidKey)]
public class HyperliquidOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="HyperliquidOrderCondition"/>.
	/// </summary>
	public HyperliquidOrderCondition()
	{
	}

	/// <summary>
	/// Trigger type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public HyperliquidOrderConditionTypes Type
	{
		get => (HyperliquidOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? HyperliquidOrderConditionTypes.StopLoss;
		set => Parameters[nameof(Type)] = value;
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
		Order = 1)]
	public decimal? ActivationPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ActivationPrice));
		set => Parameters[nameof(ActivationPrice)] = value;
	}

	/// <summary>
	/// Close price once trigger is activated. <see langword="null"/> means market close.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ClosingPriceKey,
		Description = LocalizedStrings.ClosingPriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public decimal? ClosePositionPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ClosePositionPrice));
		set => Parameters[nameof(ClosePositionPrice)] = value;
	}

	/// <summary>
	/// Use market close after trigger.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketCloseKey,
		Description = LocalizedStrings.MarketCloseKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public bool IsMarket
	{
		get => ClosePositionPrice is null;
		set
		{
			if (value)
				ClosePositionPrice = null;
		}
	}

	/// <inheritdoc />
	[DataMember]
	public bool IsTrailing
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTrailing)) ?? false;
		set => Parameters[nameof(IsTrailing)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = HyperliquidOrderConditionTypes.StopLoss;
			ActivationPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = HyperliquidOrderConditionTypes.TakeProfit;
			ActivationPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = HyperliquidOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = HyperliquidOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}
}
