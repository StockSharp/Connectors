namespace StockSharp.CryptoCom;

/// <summary>
/// Trigger order kinds.
/// </summary>
[DataContract]
public enum CryptoComOrderConditionTypes
{
	/// <summary>
	/// Stop-loss order.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey)]
	StopLoss,

	/// <summary>
	/// Take-profit order.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	TakeProfit,
}

/// <summary>
/// Reference prices used to activate trigger orders.
/// </summary>
[DataContract]
public enum CryptoComTriggerPriceTypes
{
	/// <summary>
	/// Last traded price.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LastTradeKey)]
	Last,

	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarkKey)]
	Mark,

	/// <summary>
	/// Index price.
	/// </summary>
	[EnumMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IndexKey)]
	Index,
}

/// <summary>
/// Crypto.com Exchange trigger and isolated-margin order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CryptoComExchangeKey)]
public class CryptoComOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Trigger order kind.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public CryptoComOrderConditionTypes Type
	{
		get => (CryptoComOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? CryptoComOrderConditionTypes.StopLoss;
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
	/// Limit price used after activation. A null value means market execution.
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
	/// Trigger reference price type.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PriceTypeKey,
		Description = LocalizedStrings.PriceTypeKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public CryptoComTriggerPriceTypes TriggerPriceType
	{
		get => (CryptoComTriggerPriceTypes?)Parameters.TryGetValue(nameof(TriggerPriceType)) ?? CryptoComTriggerPriceTypes.Mark;
		set => Parameters[nameof(TriggerPriceType)] = value;
	}

	/// <summary>
	/// Isolated position identifier.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IdKey,
		Description = LocalizedStrings.IdKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 4)]
	public string IsolationId
	{
		get => (string)Parameters.TryGetValue(nameof(IsolationId));
		set => Parameters[nameof(IsolationId)] = value;
	}

	/// <summary>
	/// Requested leverage.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public decimal? Leverage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Isolated margin amount.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 6)]
	public decimal? IsolatedMarginAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(IsolatedMarginAmount));
		set => Parameters[nameof(IsolatedMarginAmount)] = value;
	}

	/// <summary>
	/// Execute at market after activation.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketCloseKey,
		Description = LocalizedStrings.MarketCloseKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 7)]
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
		get => false;
		set
		{
			if (value)
				throw new NotSupportedException("Crypto.com Exchange trailing trigger orders are not supported by this condition.");
		}
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = CryptoComOrderConditionTypes.StopLoss;
			ActivationPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => ActivationPrice;
		set
		{
			Type = CryptoComOrderConditionTypes.TakeProfit;
			ActivationPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = CryptoComOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = CryptoComOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}
}
