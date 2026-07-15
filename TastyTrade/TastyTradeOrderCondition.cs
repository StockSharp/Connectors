namespace StockSharp.TastyTrade;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// tastytrade order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class TastyTradeOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;
	private bool _isExtendedHours;
	private bool _isOvernight;
	private bool _isCredit;
	private TastyTradeOrderLeg[] _legs = [];

	/// <summary>
	/// Stop trigger price.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey, Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopPrice
	{
		get => _stopPrice;
		set
		{
			_stopPrice = value;
			Parameters[nameof(StopPrice)] = value;
		}
	}

	/// <summary>
	/// Use extended-hours time in force.
	/// </summary>
	[DataMember]
	public bool IsExtendedHours
	{
		get => _isExtendedHours;
		set
		{
			_isExtendedHours = value;
			Parameters[nameof(IsExtendedHours)] = value;
		}
	}

	/// <summary>
	/// Include the overnight session.
	/// </summary>
	[DataMember]
	public bool IsOvernight
	{
		get => _isOvernight;
		set
		{
			_isOvernight = value;
			Parameters[nameof(IsOvernight)] = value;
		}
	}

	/// <summary>
	/// The order receives a net credit. Used for multi-leg orders.
	/// </summary>
	[DataMember]
	public bool IsCredit
	{
		get => _isCredit;
		set
		{
			_isCredit = value;
			Parameters[nameof(IsCredit)] = value;
		}
	}

	/// <summary>
	/// Optional legs for a multi-leg order. When empty, the order message itself defines the single leg.
	/// </summary>
	[DataMember]
	public TastyTradeOrderLeg[] Legs
	{
		get => _legs;
		set
		{
			_legs = value ?? [];
			Parameters[nameof(Legs)] = _legs;
		}
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

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

/// <summary>
/// A tastytrade multi-leg order leg.
/// </summary>
[Serializable]
[DataContract]
public sealed class TastyTradeOrderLeg
{
	/// <summary>
	/// Leg instrument identifier.
	/// </summary>
	[DataMember]
	public SecurityId SecurityId { get; set; }

	/// <summary>
	/// Leg instrument type.
	/// </summary>
	[DataMember]
	public SecurityTypes SecurityType { get; set; }

	/// <summary>
	/// Leg side.
	/// </summary>
	[DataMember]
	public Sides Side { get; set; }

	/// <summary>
	/// Leg position effect.
	/// </summary>
	[DataMember]
	public OrderPositionEffects? PositionEffect { get; set; }

	/// <summary>
	/// Leg quantity.
	/// </summary>
	[DataMember]
	public decimal Volume { get; set; }
}
