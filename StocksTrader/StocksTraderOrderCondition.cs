namespace StockSharp.StocksTrader;

/// <summary>StocksTrader protective order parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StocksTraderKey)]
public sealed class StocksTraderOrderCondition : OrderCondition,
	IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Stop-loss price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? StopLoss
	{
		get => Parameters.TryGetValue(nameof(StopLoss))?.To<decimal?>();
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>Take-profit price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? TakeProfit
	{
		get => Parameters.TryGetValue(nameof(TakeProfit))?.To<decimal?>();
		set => Parameters[nameof(TakeProfit)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLoss;
		set => StopLoss = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException(
					"StocksTrader REST API does not expose trailing stops.");
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfit;
		set => TakeProfit = value;
	}
}
