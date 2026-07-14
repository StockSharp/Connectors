namespace StockSharp.Binance;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Stop order types.
/// </summary>
[DataContract]
public enum BinanceOrderConditionTypes
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

	/// <summary>
	/// One cancels other.
	/// </summary>
	[EnumMember]
	OCO,
}

/// <summary>
/// Triggers.
/// </summary>
[DataContract]
public enum BinanceTriggerTypes
{
	/// <summary>
	/// Mark price.
	/// </summary>
	[EnumMember]
	MarkPrice,

	/// <summary>
	/// Contract price.
	/// </summary>
	[EnumMember]
	ContractPrice,
}

/// <summary>
/// <see cref="Binance"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BinanceKey)]
public class BinanceOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BinanceOrderCondition"/>.
	/// </summary>
	public BinanceOrderCondition()
	{
	}

	/// <summary>
	/// Type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public BinanceOrderConditionTypes Type
	{
		get => (BinanceOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? BinanceOrderConditionTypes.StopLoss;
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Activation price, when reached an order will be placed.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Close position price.
	/// </summary>
	[DataMember]
	public decimal? ClosePositionPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ClosePositionPrice));
		set => Parameters[nameof(ClosePositionPrice)] = value;
	}

	/// <summary>
	/// Trigger.
	/// </summary>
	public BinanceTriggerTypes? Trigger
	{
		get => (BinanceTriggerTypes?)Parameters.TryGetValue(nameof(Trigger));
		set => Parameters[nameof(Trigger)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = BinanceOrderConditionTypes.StopLoss;
			ClosePositionPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = BinanceOrderConditionTypes.TakeProfit;
			StopPrice = value;
		}
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => ClosePositionPrice;
		set
		{
			Type = BinanceOrderConditionTypes.TakeProfit;
			ClosePositionPrice = value;
		}
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = BinanceOrderConditionTypes.StopLoss;
			StopPrice = value;
		}
	}

	/// <inheritdoc />
	public bool IsTrailing
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTrailing)) ?? false;
		set => Parameters[nameof(IsTrailing)] = value;
	}
}