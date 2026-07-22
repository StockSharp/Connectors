namespace StockSharp.Ligther;

/// <summary>
/// Ligther trigger order type.
/// </summary>
[DataContract]
public enum LigtherOrderConditionTypes
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
/// <see cref="Ligther"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LigtherKey)]
public class LigtherOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
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
	public LigtherOrderConditionTypes Type
	{
		get => (LigtherOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? LigtherOrderConditionTypes.StopLoss;
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
	/// Pre-signed transaction payload for /api/v1/sendTx.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RawTxKey,
		Description = LocalizedStrings.RawTxKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public string RawTx
	{
		get => (string)Parameters.TryGetValue(nameof(RawTx));
		set => Parameters[nameof(RawTx)] = value;
	}

	/// <summary>
	/// Pre-signed cancel transaction payload for /api/v1/sendTx.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RawCancelTxKey,
		Description = LocalizedStrings.RawCancelTxKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 7)]
	public string RawCancelTx
	{
		get => (string)Parameters.TryGetValue(nameof(RawCancelTx));
		set => Parameters[nameof(RawCancelTx)] = value;
	}

	/// <summary>
	/// Optional auth token override for private requests.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 8)]
	public string AuthToken
	{
		get => (string)Parameters.TryGetValue(nameof(AuthToken));
		set => Parameters[nameof(AuthToken)] = value;
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
			Type = LigtherOrderConditionTypes.StopLoss;
			ActivationPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = LigtherOrderConditionTypes.TakeProfit;
			ActivationPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = LigtherOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = LigtherOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}
}

