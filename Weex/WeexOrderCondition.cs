namespace StockSharp.Weex;

/// <summary>
/// WEEX futures position sides.
/// </summary>
[DataContract]
[Serializable]
[JsonConverter(typeof(StringEnumConverter))]
public enum WeexPositionSides
{
	/// <summary>
	/// Position side is not specified by the exchange.
	/// </summary>
	[EnumMember(Value = "UNKNOWN")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WEEXUnknownPositionKey)]
	Unknown,

	/// <summary>
	/// Long position.
	/// </summary>
	[EnumMember(Value = "LONG")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LongKey)]
	Long,

	/// <summary>
	/// Short position.
	/// </summary>
	[EnumMember(Value = "SHORT")]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ShortKey)]
	Short,
}

/// <summary>
/// WEEX conditional order kinds.
/// </summary>
[DataContract]
[Serializable]
public enum WeexOrderConditionTypes
{
	/// <summary>
	/// Stop-loss or ordinary stop order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey)]
	StopLoss,

	/// <summary>
	/// Take-profit order.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey)]
	TakeProfit,
}

/// <summary>
/// WEEX trigger price sources.
/// </summary>
[DataContract]
[Serializable]
public enum WeexTriggerPriceTypes
{
	/// <summary>
	/// Last contract price.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LastTradeKey)]
	LastPrice,

	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarkKey)]
	MarkPrice,
}

/// <summary>
/// WEEX futures conditional order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WEEXKey)]
public class WeexOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Conditional order kind.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public WeexOrderConditionTypes Type
	{
		get => (WeexOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? WeexOrderConditionTypes.StopLoss;
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Trigger price.
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
	/// Limit price used after activation. A null value selects market execution.
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
	/// Futures position side. When omitted, it is inferred from order side and position effect.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public WeexPositionSides? PositionSide
	{
		get => (WeexPositionSides?)Parameters.TryGetValue(nameof(PositionSide));
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// Trigger price source.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceTypeKey,
		Description = LocalizedStrings.PriceTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public WeexTriggerPriceTypes TriggerPriceType
	{
		get => (WeexTriggerPriceTypes?)Parameters.TryGetValue(nameof(TriggerPriceType)) ?? WeexTriggerPriceTypes.LastPrice;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}

	/// <summary>
	/// Optional take-profit trigger attached to an ordinary futures order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public decimal? TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice));
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>
	/// Optional stop-loss trigger attached to an ordinary futures order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public decimal? StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice));
		set => Parameters[nameof(StopLossPrice)] = value;
	}

	/// <inheritdoc />
	public bool IsTrailing
	{
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException("WEEX trailing trigger orders are not supported by this condition.");
		}
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = WeexOrderConditionTypes.StopLoss;
			ActivationPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = WeexOrderConditionTypes.TakeProfit;
			ActivationPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = WeexOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = WeexOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}
}
