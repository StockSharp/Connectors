namespace StockSharp.MetaApi;

/// <summary>MetaApi pending-order and protection parameters.</summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MetaApiKey)]
public sealed class MetaApiOrderCondition : OrderCondition,
	IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>Stop-order activation price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActivationPriceKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? ActivationPrice
	{
		get => Parameters.TryGetValue(nameof(ActivationPrice))?.To<decimal?>();
		set => Parameters[nameof(ActivationPrice)] = value;
	}

	/// <summary>Stop-loss price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLossKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
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
		Order = 2)]
	public decimal? TakeProfit
	{
		get => Parameters.TryGetValue(nameof(TakeProfit))?.To<decimal?>();
		set => Parameters[nameof(TakeProfit)] = value;
	}

	/// <summary>Optional MetaTrader magic number.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdentifierKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public long? Magic
	{
		get => Parameters.TryGetValue(nameof(Magic))?.To<long?>();
		set => Parameters[nameof(Magic)] = value;
	}

	/// <summary>Optional broker-visible order comment.</summary>
	[DataMember]
	public string Comment
	{
		get => Parameters.TryGetValue(nameof(Comment))?.To<string>();
		set => Parameters[nameof(Comment)] = value;
	}

	/// <summary>Optional client identifier used to correlate MetaApi trades.</summary>
	[DataMember]
	public string ClientId
	{
		get => Parameters.TryGetValue(nameof(ClientId))?.To<string>();
		set => Parameters[nameof(ClientId)] = value;
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
					"MetaApi trailing stop parameters are not exposed by this connector.");
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
