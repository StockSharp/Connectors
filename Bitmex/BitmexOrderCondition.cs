namespace StockSharp.Bitmex;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

/// <summary>
/// Stop order types.
/// </summary>
[Serializable]
[DataContract]
public enum BitmexOrderTypes
{
	/// <summary>
	/// The market order is automatically registered after reaching the stop price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopOrderTypeKey)]
	[EnumMember]
	Stop,

	/// <summary>
	/// The limit order is automatically registered after reaching the stop price.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopLimitKey)]
	[EnumMember]
	StopLimit,

	/// <summary>
	/// With the market price when the condition is fulfilled.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MarketOnTouchKey)]
	[EnumMember]
	MarketIfTouched,

	/// <summary>
	/// With the specified price when the condition is fulfilled.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LimitOnTouchKey)]
	[EnumMember]
	LimitIfTouched,
}

/// <summary>
/// Contingency types.
/// </summary>
[Serializable]
[DataContract]
public enum BitmexOrderContingencyTypes
{
	/// <summary>
	/// </summary>
	[EnumMember]
	OneCancelsTheOther,
	/// <summary>
	/// </summary>
	[EnumMember]
	OneTriggersTheOther,
	/// <summary>
	/// </summary>
	[EnumMember]
	OneUpdatesTheOtherAbsolute,
	/// <summary>
	/// </summary>
	[EnumMember]
	OneUpdatesTheOtherProportional
}

/// <summary>
/// Peg price types.
/// </summary>
[Serializable]
[DataContract]
public enum BitmexOrderPegPriceTypes
{
	/// <summary>
	/// </summary>
	[EnumMember]
	LastPeg,
	/// <summary>
	/// </summary>
	[EnumMember]
	MidPricePeg,
	/// <summary>
	/// </summary>
	[EnumMember]
	MarketPeg,
	/// <summary>
	/// </summary>
	[EnumMember]
	PrimaryPeg,
	/// <summary>
	/// </summary>
	[EnumMember]
	TrailingStopPeg
}

/// <summary>
/// Execution instructions.
/// </summary>
[Flags]
[Serializable]
[DataContract]
public enum BitmexOrderExecInstructions
{
	/// <summary>
	/// </summary>
	[EnumMember]
	AllOrNone = 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	MarkPrice = AllOrNone << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	IndexPrice = MarkPrice << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	LastPrice = IndexPrice << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	Close = LastPrice << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	ReduceOnly = Close << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	Fixed = ReduceOnly << 1,
	/// <summary>
	/// </summary>
	[EnumMember]
	ParticipateDoNotInitiate = Fixed << 1,
}

/// <summary>
/// <see cref="Bitmex"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitmexKey)]
public class BitmexOrderCondition : BaseWithdrawOrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="BitmexOrderCondition"/>.
	/// </summary>
	public BitmexOrderCondition()
	{
	}

	/// <summary>
	/// Stop type.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopTypeKey,
		Description = LocalizedStrings.StopTypeDescKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public BitmexOrderTypes? StopType
	{
		get => (BitmexOrderTypes?)Parameters.TryGetValue(nameof(StopType));
		set => Parameters[nameof(StopType)] = value;
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
	/// Client Order Link ID for contingent orders.
	/// </summary>
	public string ClOrdLinkId
	{
		get => (string)Parameters.TryGetValue(nameof(ClOrdLinkId));
		set => Parameters[nameof(ClOrdLinkId)] = value;
	}

	/// <summary>
	/// Trailing offset from the current price.
	/// </summary>
	public decimal? PegOffsetValue
	{
		get => (decimal?)Parameters.TryGetValue(nameof(PegOffsetValue));
		set => Parameters[nameof(PegOffsetValue)] = value;
	}

	/// <summary>
	/// Peg price type.
	/// </summary>
	public BitmexOrderPegPriceTypes? PegPriceType
	{
		get => (BitmexOrderPegPriceTypes?)Parameters.TryGetValue(nameof(PegPriceType));
		set => Parameters[nameof(PegPriceType)] = value;
	}

	/// <summary>
	/// Contingency type for use with <see cref="ClOrdLinkId"/>.
	/// </summary>
	public BitmexOrderContingencyTypes? ContingencyType
	{
		get => (BitmexOrderContingencyTypes?)Parameters.TryGetValue(nameof(ContingencyType));
		set => Parameters[nameof(ContingencyType)] = value;
	}

	/// <summary>
	/// Execution instruction.
	/// </summary>
	public BitmexOrderExecInstructions? ExecInst
	{
		get => (BitmexOrderExecInstructions?)Parameters.TryGetValue(nameof(ExecInst));
		set => Parameters[nameof(ExecInst)] = value;
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