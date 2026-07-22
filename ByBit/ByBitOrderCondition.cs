namespace StockSharp.ByBit;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Trigger direction in Bybit.
/// </summary>
[Serializable]
[DataContract]
public enum ByBitTriggerDirections
{
	/// <summary>
	/// Price rises.
	/// </summary>
	[EnumMember]
	Rise,

	/// <summary>
	/// Price falls.
	/// </summary>
	[EnumMember]
	Fall
}

/// <summary>
/// Price type that triggers the order in Bybit.
/// </summary>
[Serializable]
[DataContract]
public enum ByBitTriggerBy
{
	/// <summary>
	/// Last traded price.
	/// </summary>
	[EnumMember]
	LastPrice,

	/// <summary>
	/// Index price.
	/// </summary>
	[EnumMember]
	IndexPrice,

	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember]
	MarkPrice
}

/// <summary>
/// Position index in Bybit.
/// </summary>
[Serializable]
[DataContract]
public enum ByBitPositionIdx
{
	/// <summary>
	/// One-way position.
	/// </summary>
	[EnumMember]
	OneWay,

	/// <summary>
	/// Buy-side position.
	/// </summary>
	[EnumMember]
	BuySide,

	/// <summary>
	/// Sell-side position.
	/// </summary>
	[EnumMember]
	SellSide
}

/// <summary>
/// Self-Matching Prevention (SMP) type in Bybit.
/// </summary>
[Serializable]
[DataContract]
public enum ByBitSmpTypes
{
	/// <summary>
	/// No SMP.
	/// </summary>
	[EnumMember]
	None,

	/// <summary>
	/// Cancel maker.
	/// </summary>
	[EnumMember]
	CancelMaker,

	/// <summary>
	/// Cancel taker.
	/// </summary>
	[EnumMember]
	CancelTaker,

	/// <summary>
	/// Cancel both.
	/// </summary>
	[EnumMember]
	CancelBoth
}

/// <summary>
/// Take Profit and Stop Loss mode in Bybit.
/// </summary>
[Serializable]
[DataContract]
public enum ByBitTpSlModes
{
	/// <summary>
	/// Full execution.
	/// </summary>
	[EnumMember]
	Full,

	/// <summary>
	/// Partial execution.
	/// </summary>
	[EnumMember]
	Partial
}

/// <summary>
/// Market unit in Bybit.
/// </summary>
public enum ByBitMarketUnits
{
	/// <summary>
	/// Base coin.
	/// </summary>
	[EnumMember]
	BaseCoin,

	/// <summary>
	/// Quote coin.
	/// </summary>
	[EnumMember]
	QuoteCoin
}

/// <summary>
/// <see cref="ByBit"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ByBitKey)]
public class ByBitOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ByBitOrderCondition"/>.
	/// </summary>
	public ByBitOrderCondition()
	{
	}

	/// <summary>
	/// Stop-price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceValueKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Market unit.
	/// </summary>
	public ByBitMarketUnits? MarketUnit
	{
		get => (ByBitMarketUnits?)Parameters.TryGetValue(nameof(MarketUnit));
		set => Parameters[nameof(MarketUnit)] = value;
	}

	/// <summary>
	/// Trigger direction.
	/// </summary>
	public ByBitTriggerDirections? TriggerDirection
	{
		get => (ByBitTriggerDirections?)Parameters.TryGetValue(nameof(TriggerDirection));
		set => Parameters[nameof(TriggerDirection)] = value;
	}

	/// <summary>
	/// Trigger price.
	/// </summary>
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>
	/// Trigger by.
	/// </summary>
	public ByBitTriggerBy? TriggerBy
	{
		get => (ByBitTriggerBy?)Parameters.TryGetValue(nameof(TriggerBy));
		set => Parameters[nameof(TriggerBy)] = value;
	}

	/// <summary>
	/// Implied volatility.
	/// </summary>
	public decimal? IV
	{
		get => (decimal?)Parameters.TryGetValue(nameof(IV));
		set => Parameters[nameof(IV)] = value;
	}

	/// <summary>
	/// Position index.
	/// </summary>
	public ByBitPositionIdx? PositionIdx
	{
		get => (ByBitPositionIdx?)Parameters.TryGetValue(nameof(PositionIdx));
		set => Parameters[nameof(PositionIdx)] = value;
	}

	/// <summary>
	/// Take profit price.
	/// </summary>
	public decimal? TakeProfit
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfit));
		set => Parameters[nameof(TakeProfit)] = value;
	}

	/// <summary>
	/// Stop loss price.
	/// </summary>
	public decimal? StopLoss
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLoss));
		set => Parameters[nameof(StopLoss)] = value;
	}

	/// <summary>
	/// Take profit trigger by.
	/// </summary>
	public ByBitTriggerBy? TpTriggerBy
	{
		get => (ByBitTriggerBy?)Parameters.TryGetValue(nameof(TpTriggerBy));
		set => Parameters[nameof(TpTriggerBy)] = value;
	}

	/// <summary>
	/// Stop loss trigger by.
	/// </summary>
	public ByBitTriggerBy? SlTriggerBy
	{
		get => (ByBitTriggerBy?)Parameters.TryGetValue(nameof(SlTriggerBy));
		set => Parameters[nameof(SlTriggerBy)] = value;
	}

	/// <summary>
	/// Close on trigger.
	/// </summary>
	public bool? CloseOnTrigger
	{
		get => (bool?)Parameters.TryGetValue(nameof(CloseOnTrigger));
		set => Parameters[nameof(CloseOnTrigger)] = value;
	}

	/// <summary>
	/// SMP type.
	/// </summary>
	public ByBitSmpTypes? SmpType
	{
		get => (ByBitSmpTypes?)Parameters.TryGetValue(nameof(SmpType));
		set => Parameters[nameof(SmpType)] = value;
	}

	/// <summary>
	/// MMP value.
	/// </summary>
	public bool? Mmp
	{
		get => (bool?)Parameters.TryGetValue(nameof(Mmp));
		set => Parameters[nameof(Mmp)] = value;
	}

	/// <summary>
	/// Take profit and stop loss mode.
	/// </summary>
	public ByBitTpSlModes? TpSlMode
	{
		get => (ByBitTpSlModes?)Parameters.TryGetValue(nameof(TpSlMode));
		set => Parameters[nameof(TpSlMode)] = value;
	}

	/// <summary>
	/// Take profit limit price.
	/// </summary>
	public decimal? TpLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TpLimitPrice));
		set => Parameters[nameof(TpLimitPrice)] = value;
	}

	/// <summary>
	/// Stop loss limit price.
	/// </summary>
	public decimal? SlLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SlLimitPrice));
		set => Parameters[nameof(SlLimitPrice)] = value;
	}

	/// <summary>
	/// Take profit order type.
	/// </summary>
	public OrderTypes? TpOrderType
	{
		get => (OrderTypes?)Parameters.TryGetValue(nameof(TpOrderType));
		set => Parameters[nameof(TpOrderType)] = value;
	}

	/// <summary>
	/// Stop loss order type.
	/// </summary>
	public OrderTypes? SlOrderType
	{
		get => (OrderTypes?)Parameters.TryGetValue(nameof(SlOrderType));
		set => Parameters[nameof(SlOrderType)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}