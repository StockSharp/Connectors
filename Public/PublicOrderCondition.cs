namespace StockSharp.Public;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Public.com order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class PublicOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;
	private PublicEquityMarketSessions? _marketSession;
	private bool? _isMarginEnabled;
	private PublicOpenCloseIndicators? _openCloseIndicator;
	private PublicOrderLeg[] _legs = [];

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
	/// Equity trading session.
	/// </summary>
	[DataMember]
	public PublicEquityMarketSessions? MarketSession
	{
		get => _marketSession;
		set
		{
			_marketSession = value;
			Parameters[nameof(MarketSession)] = value;
		}
	}

	/// <summary>
	/// Whether margin buying power is enabled for the order.
	/// </summary>
	[DataMember]
	public bool? IsMarginEnabled
	{
		get => _isMarginEnabled;
		set
		{
			_isMarginEnabled = value;
			Parameters[nameof(IsMarginEnabled)] = value;
		}
	}

	/// <summary>
	/// Open or close intent for an option or short-equity order.
	/// </summary>
	[DataMember]
	public PublicOpenCloseIndicators? OpenCloseIndicator
	{
		get => _openCloseIndicator;
		set
		{
			_openCloseIndicator = value;
			Parameters[nameof(OpenCloseIndicator)] = value;
		}
	}

	/// <summary>
	/// Multi-leg option order legs.
	/// </summary>
	[DataMember]
	public PublicOrderLeg[] Legs
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
/// Public.com multi-leg order leg.
/// </summary>
[Serializable]
[DataContract]
public sealed class PublicOrderLeg
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
	/// Open or close intent.
	/// </summary>
	[DataMember]
	public PublicOpenCloseIndicators? OpenCloseIndicator { get; set; }

	/// <summary>
	/// Leg ratio quantity.
	/// </summary>
	[DataMember]
	public int RatioQuantity { get; set; } = 1;
}
