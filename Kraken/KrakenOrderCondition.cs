namespace StockSharp.Kraken;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Stop order types.
/// </summary>
public enum KrakenOrderConditionTypes
{
	/// <summary>
	/// Stop-loss.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopOrderTypeKey)]
	[EnumMember]
	StopLoss,

	/// <summary>
	/// Take-profit.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,

	/// <summary>
	/// Stop-loss and take-profit.
	/// </summary>
	//[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	StopLossTakeProfit,
}

/// <summary>
/// <see cref="Kraken"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KrakenKey)]
public class KrakenOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition, ITakeProfitOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="KrakenOrderCondition"/>.
	/// </summary>
	public KrakenOrderCondition()
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
	public KrakenOrderConditionTypes Type
	{
		get => (KrakenOrderConditionTypes?)Parameters.TryGetValue(nameof(Type)) ?? KrakenOrderConditionTypes.StopLoss;
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
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Trailing stop-loss.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TrailingKey,
		Description = LocalizedStrings.TrailingStopLossKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 2)]
	public bool IsTrailing
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsTrailing)) ?? false;
		set => Parameters[nameof(IsTrailing)] = value;
	}

	/// <summary>
	/// Leverage.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LeverageKey,
		Description = LocalizedStrings.MarginLeverageKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public string Leverage
	{
		get => (string)Parameters.TryGetValue(nameof(Leverage));
		set => Parameters[nameof(Leverage)] = value;
	}

	/// <summary>
	/// Order flags.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OrderFlagsKey,
		Description = LocalizedStrings.OrderFlagsKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 3)]
	public string OrderFlags
	{
		get => (string)Parameters.TryGetValue(nameof(OrderFlags));
		set => Parameters[nameof(OrderFlags)] = value;
	}

	/// <summary>
	/// Start time.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ActivationTimeKey,
		Description = LocalizedStrings.ActivationTimeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 5)]
	public string StartTime
	{
		get => (string)Parameters.TryGetValue(nameof(StartTime));
		set => Parameters[nameof(StartTime)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			var prev = (KrakenOrderConditionTypes?)Parameters.TryGetValue(nameof(Type));
			if (prev == KrakenOrderConditionTypes.TakeProfit || prev == KrakenOrderConditionTypes.StopLossTakeProfit)
				Type = KrakenOrderConditionTypes.StopLossTakeProfit;
			else
				Type = KrakenOrderConditionTypes.StopLoss;

			StopPrice = value;
		}
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => IsTrailing;
		set => IsTrailing = value;
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice { get; set; }

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			var prev = (KrakenOrderConditionTypes?)Parameters.TryGetValue(nameof(Type));
			if (prev == KrakenOrderConditionTypes.StopLoss || prev == KrakenOrderConditionTypes.StopLossTakeProfit)
				Type = KrakenOrderConditionTypes.StopLossTakeProfit;
			else
				Type = KrakenOrderConditionTypes.TakeProfit;

			StopPrice = value;
		}
	}
}