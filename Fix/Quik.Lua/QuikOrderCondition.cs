namespace StockSharp.Fix.Quik.Lua;

using System.Runtime.Serialization;

/// <summary>
/// Stop price conditions relative to the price of the last trade of the instrument.
/// </summary>
[Serializable]
[DataContract]
public enum QuikStopPriceConditions
{
	/// <summary>
	/// Greater than or equal.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MoreOrEqualKey)]
	[EnumMember]
	MoreOrEqual,

	/// <summary>
	/// Less than or equal.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LessOrEqualKey)]
	[EnumMember]
	LessOrEqual,
}

/// <summary>
/// Types of order conditions specific to <see cref="QuikOrderCondition"/>.
/// </summary>
[Serializable]
[DataContract]
public enum QuikOrderConditionTypes
{
	/// <summary>
	/// Two orders for the same instrument, identical in direction and volume. The first order is of type "Stop-limit", the second is a limit order.
	/// When one order is executed, the other is canceled.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LinkedOrderKey)]
	[EnumMember]
	LinkedOrder,

	/// <summary>
	/// An order of type "Stop-limit" whose stop-price condition is checked against one instrument, while another instrument is specified in the resulting limit order.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecurityKey)]
	[EnumMember]
	OtherSecurity,

	/// <summary>
	/// A stop order that generates a limit order upon activation.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopLimitKey)]
	[EnumMember]
	StopLimit,

	/// <summary>
	/// An order with the condition: "Execute when the price worsens by the specified value from the reached maximum (for sell) or minimum (for buy)."
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitKey)]
	[EnumMember]
	TakeProfit,

	/// <summary>
	/// An order with two conditions: "take-profit" if the last trade price, after reaching a maximum, worsens by more than the specified offset;
	/// "stop-limit" if the last trade price worsens to the specified level.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TakeProfitStopLossKey)]
	[EnumMember]
	TakeProfitStopLimit,
}

///// <summary>
///// Result of execution for orders specific to <see cref="QuikTrader"/>.
///// </summary>
//[Serializable]
//[System.Runtime.Serialization.DataContract]
//public enum QuikOrderConditionResults
//{
//	/// <summary>
//	/// The order was accepted by the trading system.
//	/// </summary>
//	[EnumMember]
//	SentToTS,
//
//	/// <summary>
//	/// The order was rejected by the trading system.
//	/// </summary>
//	[EnumMember]
//	RejectedByTS,
//
//	/// <summary>
//	/// The order was canceled by the user.
//	/// </summary>
//	[EnumMember]
//	Killed,
//
//	/// <summary>
//	/// Insufficient client funds to execute the order.
//	/// </summary>
//	[EnumMember]
//	LimitControlFailed,
//
//	/// <summary>
//	/// The limit order linked to the stop order was canceled by the user.
//	/// </summary>
//	[EnumMember]
//	LinkedOrderKilled,
//
//	/// <summary>
//	/// The linked limit order was satisfied by the trading system.
//	/// </summary>
//	[EnumMember]
//	LinkedOrderFilled,
//
//	/// <summary>
//	/// The activation condition did not occur. Parameter for orders of types "Take-profit" and "On execution".
//	/// </summary>
//	[EnumMember]
//	WaitingForActivation,
//
//	/// <summary>
//	/// The activation condition has occurred; calculation of the minimum/maximum price has started. Parameter for orders of types "Take-profit" and "Take-profit by order".
//	/// </summary>
//	[EnumMember]
//	CalculateMinMax,
//
//	/// <summary>
//	/// The order was activated for a partial volume due to partial execution of the conditional order; calculation of the minimum/maximum price has started.
//	/// Parameter for orders of type "Take-profit by order" with the flag "Partial execution of the order is taken into account" enabled.
//	/// </summary>
//	[EnumMember]
//	CalculateMinMaxAndWaitForActivation,
//}

/// <summary>
/// Order condition specific to <see cref="Quik"/>.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.QuikKey)]
public class QuikOrderCondition : OrderCondition,
                                  IStopLossOrderCondition, ITakeProfitOrderCondition,
								  IRepoOrderCondition, INtmOrderCondition
{
	/// <summary>
	/// Create <see cref="QuikOrderCondition"/>.
	/// </summary>
	public QuikOrderCondition()
	{
		IsRepo = false;
		IsNtm = false;
		RepoInfo = new RepoOrderInfo();
		NtmInfo = new NtmOrderInfo();
	}

	/// <summary>
	/// Stop-order type.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopOrderTypeKey,
		Description = LocalizedStrings.StopOrderTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public QuikOrderConditionTypes? Type
	{
		get => (QuikOrderConditionTypes?)Parameters.TryGetValue(nameof(Type));
		set => Parameters[nameof(Type)] = value;
	}

	/// <summary>
	/// Instrument identifier for stop orders with a condition based on another instrument.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecurityIdKey,
		Description = LocalizedStrings.OtherSecurityIdKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public SecurityId? OtherSecurityId
	{
		get => (SecurityId?)Parameters.TryGetValue(nameof(OtherSecurityId));
		set => Parameters[nameof(OtherSecurityId)] = value;
	}

	/// <summary>
	/// Stop-price condition. Used for orders of type "Stop price by another instrument".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConditionKey,
		Description = LocalizedStrings.StopPriceConditionKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public QuikStopPriceConditions? StopPriceCondition
	{
		get => (QuikStopPriceConditions?)Parameters.TryGetValue(nameof(StopPriceCondition));
		set => Parameters[nameof(StopPriceCondition)] = value;
	}

	/// <summary>
	/// Stop price that defines the activation condition of the stop order. For example, for orders of type "Stop price by another instrument", the condition looks like:
	/// "If price &lt;=" (or ">=") and means execution when the last trade price for the other instrument crosses the specified value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Stop-limit price. Similar to <see cref="StopPrice"/>, but used only for the order type "Take-profit and stop-limit".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLimitPriceKey,
		Description = LocalizedStrings.StopLimitPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? StopLimitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLimitPrice));
		set => Parameters[nameof(StopLimitPrice)] = value;
	}

	/// <summary>
	/// Execute a "Stop-limit" order at market price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IsMarketStopLimitKey,
		Description = LocalizedStrings.IsMarketStopLimitKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public bool? IsMarketStopLimit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketStopLimit));
		set => Parameters[nameof(IsMarketStopLimit)] = value;
	}

	/// <summary>
	/// The condition is checked only during the specified time range (if <see langword="null"/>, do not check).
	/// Used for order types "Take-profit and stop-limit" and "Take-profit and stop-limit by order".
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeKey,
		Description = LocalizedStrings.TimeKey,
		GroupName = LocalizedStrings.ParametersKey)]
	//[Editor(typeof(RangeEditor<DateTime>), typeof(RangeEditor<DateTime>))]
	public (DateTime from, DateTime to) ActiveTime
	{
		get => ((DateTime, DateTime))Parameters.TryGetValue(nameof(ActiveTime));
		set => Parameters[nameof(ActiveTime)] = value;
	}

	/// <summary>
	/// Conditional order identifier.
	/// </summary>
	public long? ConditionOrderId
	{
		get => (long?)Parameters.TryGetValue(nameof(ConditionOrderId));
		set => Parameters[nameof(ConditionOrderId)] = value;
	}

	/// <summary>
	/// Conditional order side.
	/// </summary>
	public Sides? ConditionOrderSide
	{
		get => (Sides?)Parameters.TryGetValue(nameof(ConditionOrderSide));
		set => Parameters[nameof(ConditionOrderSide)] = value;
	}

	/// <summary>
	/// Partial execution is taken into account. The "on execution" order will be activated upon partial execution of the conditional order <see cref="ConditionOrderId"/>.
	/// If <see langword="false"/> (or <see langword="null"/>), the "on execution" order is activated only upon full execution of the conditional order <see cref="ConditionOrderId"/>.
	/// </summary>
	[DataMember]
	public bool? ConditionOrderPartiallyMatched
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionOrderPartiallyMatched));
		set => Parameters[nameof(ConditionOrderPartiallyMatched)] = value;
	}

	/// <summary>
	/// Use the executed volume of the conditional order as the quantity of the placed stop order. The number of instruments in the "on execution" order
	/// is taken from the executed volume of the conditional order <see cref="ConditionOrderId"/>. If <see langword="false"/> (or <see langword="null"/>), the order volume is explicitly specified in <see cref="ExecutionMessage.OrderVolume"/>.
	/// </summary>
	[DataMember]
	public bool? ConditionOrderUseMatchedBalance
	{
		get => (bool?)Parameters.TryGetValue(nameof(ConditionOrderUseMatchedBalance));
		set => Parameters[nameof(ConditionOrderUseMatchedBalance)] = value;
	}

	/// <summary>
	/// Price of the linked limit order.
	/// </summary>
	[DataMember]
	public decimal? LinkedOrderPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(LinkedOrderPrice));
		set => Parameters[nameof(LinkedOrderPrice)] = value;
	}

	/// <summary>
	/// Cancel the stop order upon partial execution of the linked limit order.
	/// </summary>
	[DataMember]
	public bool? LinkedOrderCancel
	{
		get => (bool?)Parameters.TryGetValue(nameof(LinkedOrderCancel));
		set => Parameters[nameof(LinkedOrderCancel)] = value;
	}

	/// <summary>
	/// Offset value from the maximum (minimum) of the last trade price.
	/// </summary>
	[DataMember]
	public Unit Offset
	{
		get => (Unit)Parameters.TryGetValue(nameof(Offset));
		set => Parameters[nameof(Offset)] = value;
	}

	/// <summary>
	/// Protective spread value.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpreadKey,
		Description = LocalizedStrings.SpreadKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public Unit Spread
	{
		get => (Unit)Parameters.TryGetValue(nameof(Spread));
		set => Parameters[nameof(Spread)] = value;
	}

	/// <summary>
	/// Execute a "Take-profit" order at market price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IsMarketTakeProfitKey,
		Description = LocalizedStrings.IsMarketTakeProfitKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public bool? IsMarketTakeProfit
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsMarketTakeProfit));
		set => Parameters[nameof(IsMarketTakeProfit)] = value;
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseKey,
		Description = LocalizedStrings.RepoKey,
		GroupName = LocalizedStrings.RepoKey,
		Order = 100)]
	public bool IsRepo
	{
		get => (bool)Parameters[nameof(IsRepo)];
		set => Parameters[nameof(IsRepo)] = value;
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RepoKey,
		Description = LocalizedStrings.RepoInfoKey,
		GroupName = LocalizedStrings.RepoKey,
		Order = 101)]
	public RepoOrderInfo RepoInfo
	{
		get => (RepoOrderInfo)Parameters[nameof(RepoInfo)];
		set => Parameters[nameof(RepoInfo)] = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.UseKey,
		Description = LocalizedStrings.NtmDescKey,
		GroupName = LocalizedStrings.NtmKey,
		Order = 200)]
	public bool IsNtm
	{
		get => (bool)Parameters[nameof(IsNtm)];
		set => Parameters[nameof(IsNtm)] = value;
	}

	/// <inheritdoc />
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.NtmKey,
		Description = LocalizedStrings.NtmInfoKey,
		GroupName = LocalizedStrings.NtmKey,
		Order = 201)]
	public NtmOrderInfo NtmInfo
	{
		get => (NtmOrderInfo)Parameters[nameof(NtmInfo)];
		set => Parameters[nameof(NtmInfo)] = value ?? throw new ArgumentNullException(nameof(value));
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => StopLimitPrice;
		set => StopLimitPrice = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = QuikOrderConditionTypes.StopLimit;
			StopPrice = value;
		}
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	decimal? ITakeProfitOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set
		{
			Type = QuikOrderConditionTypes.TakeProfit;
			StopPrice = value;
		}
	}
}