namespace StockSharp.EdgeX;

/// <summary>
/// EdgeX trigger order type.
/// </summary>
[DataContract]
public enum EdgeXOrderConditionTypes
{
	/// <summary>
	/// Stop-loss.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey)]
	[EnumMember]
	StopLoss,

	/// <summary>
	/// Take-profit.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,
}

/// <summary>
/// <see cref="EdgeX"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EdgeXKey)]
public class EdgeXOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Trigger type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public EdgeXOrderConditionTypes Type
	{
		get => (EdgeXOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? EdgeXOrderConditionTypes.StopLoss;
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
	/// Trigger close price.
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

	/// <summary>
	/// Reduce-only flag.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PosConditionReduceOnlyKey,
		Description = LocalizedStrings.PosConditionReduceOnlyDetailsKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public bool ReduceOnly
	{
		get => (bool?)Parameters.TryGetValue(nameof(ReduceOnly)) ?? false;
		set => Parameters[nameof(ReduceOnly)] = value;
	}

	/// <summary>
	/// Position side (LONG/SHORT/BOTH).
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.SideKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public string PositionSide
	{
		get => (string)Parameters.TryGetValue(nameof(PositionSide));
		set => Parameters[nameof(PositionSide)] = value;
	}

	/// <summary>
	/// edgeX L2 signature.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2SignatureKey,
		Description = LocalizedStrings.L2SignatureKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public string L2Signature
	{
		get => (string)Parameters.TryGetValue(nameof(L2Signature));
		set => Parameters[nameof(L2Signature)] = value;
	}

	/// <summary>
	/// edgeX L2 nonce.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2NonceKey,
		Description = LocalizedStrings.L2NonceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 7)]
	public string L2Nonce
	{
		get => (string)Parameters.TryGetValue(nameof(L2Nonce));
		set => Parameters[nameof(L2Nonce)] = value;
	}

	/// <summary>
	/// edgeX L2 expire time.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2ExpireKey,
		Description = LocalizedStrings.L2ExpireKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 8)]
	public string L2ExpireTime
	{
		get => (string)Parameters.TryGetValue(nameof(L2ExpireTime));
		set => Parameters[nameof(L2ExpireTime)] = value;
	}

	/// <summary>
	/// edgeX L2 order value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2ValueKey,
		Description = LocalizedStrings.L2ValueKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 9)]
	public string L2Value
	{
		get => (string)Parameters.TryGetValue(nameof(L2Value));
		set => Parameters[nameof(L2Value)] = value;
	}

	/// <summary>
	/// edgeX L2 order size.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2SizeKey,
		Description = LocalizedStrings.L2SizeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 10)]
	public string L2Size
	{
		get => (string)Parameters.TryGetValue(nameof(L2Size));
		set => Parameters[nameof(L2Size)] = value;
	}

	/// <summary>
	/// edgeX L2 fee limit.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.L2FeeLimitKey,
		Description = LocalizedStrings.L2FeeLimitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 11)]
	public string L2LimitFee
	{
		get => (string)Parameters.TryGetValue(nameof(L2LimitFee));
		set => Parameters[nameof(L2LimitFee)] = value;
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
			Type = EdgeXOrderConditionTypes.StopLoss;
			ActivationPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = EdgeXOrderConditionTypes.TakeProfit;
			ActivationPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = EdgeXOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = EdgeXOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}
}

