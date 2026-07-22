namespace StockSharp.CoinW;

/// <summary>
/// CoinW futures margin modes.
/// </summary>
[DataContract]
[Serializable]
public enum CoinWFuturesMarginModes
{
	/// <summary>
	/// Isolated margin.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CoinWIsolatedMarginKey)]
	Isolated,

	/// <summary>
	/// Cross margin.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CoinWCrossMarginKey)]
	Cross,
}

/// <summary>
/// CoinW futures quantity units.
/// </summary>
[DataContract]
[Serializable]
public enum CoinWFuturesQuantityUnits
{
	/// <summary>
	/// Quote currency amount.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey)]
	QuoteCurrency,

	/// <summary>
	/// Number of contracts.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey)]
	Contracts,

	/// <summary>
	/// Base currency amount.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.VolumeKey)]
	BaseCurrency,
}

/// <summary>
/// CoinW futures order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinWKey)]
public class CoinWOrderCondition : OrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Position leverage.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.LeverageKey, GroupName = LocalizedStrings.ParametersKey, Order = 0)]
	public int Leverage
	{
		get => (int?)Parameters.TryGetValue(nameof(Leverage)) ?? 1;
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Position margin mode.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey,
		Description = LocalizedStrings.MarginKey, GroupName = LocalizedStrings.ParametersKey, Order = 1)]
	public CoinWFuturesMarginModes MarginMode
	{
		get => (CoinWFuturesMarginModes?)Parameters.TryGetValue(nameof(MarginMode)) ?? CoinWFuturesMarginModes.Isolated;
		set => Parameters[nameof(MarginMode)] = value;
	}

	/// <summary>
	/// Unit used for the order quantity.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey, GroupName = LocalizedStrings.ParametersKey, Order = 2)]
	public CoinWFuturesQuantityUnits QuantityUnit
	{
		get => (CoinWFuturesQuantityUnits?)Parameters.TryGetValue(nameof(QuantityUnit)) ?? CoinWFuturesQuantityUnits.BaseCurrency;
		set => Parameters[nameof(QuantityUnit)] = value;
	}

	/// <summary>
	/// Existing position identifier used by the dedicated close-position endpoint.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PositionKey,
		Description = LocalizedStrings.PositionKey, GroupName = LocalizedStrings.ParametersKey, Order = 3)]
	public string PositionId
	{
		get => (string)Parameters.TryGetValue(nameof(PositionId));
		set => Parameters[nameof(PositionId)] = value;
	}

	/// <summary>
	/// Fraction of the position to close, from greater than 0 through 1.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PercentKey,
		Description = LocalizedStrings.PercentKey, GroupName = LocalizedStrings.ParametersKey, Order = 4)]
	public decimal? CloseRate
	{
		get => (decimal?)Parameters.TryGetValue(nameof(CloseRate));
		set => Parameters[nameof(CloseRate)] = value;
	}

	/// <summary>
	/// Trigger price for a planned order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.ParametersKey, Order = 5)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Optional take-profit price attached to a futures order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey,
		Description = LocalizedStrings.TakeProfitKey, GroupName = LocalizedStrings.ParametersKey, Order = 6)]
	public decimal? TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice));
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>
	/// Optional stop-loss price attached to a futures order.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLossKey,
		Description = LocalizedStrings.StopLossKey, GroupName = LocalizedStrings.ParametersKey, Order = 7)]
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
				throw new NotSupportedException("CoinW trailing orders require a separate endpoint.");
		}
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopLossPrice;
		set => StopLossPrice = value;
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => TakeProfitPrice;
		set => TakeProfitPrice = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set
		{
			if (value is not null)
				throw new NotSupportedException("CoinW attaches stop-loss orders directly to a futures position.");
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set
		{
			if (value is not null)
				throw new NotSupportedException("CoinW attaches take-profit orders directly to a futures position.");
		}
	}
}
