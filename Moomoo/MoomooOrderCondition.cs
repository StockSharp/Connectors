namespace StockSharp.Moomoo;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Moomoo order condition.
/// </summary>
[Serializable]
[DataContract]
public sealed class MoomooOrderCondition : OrderCondition, IStopLossOrderCondition
{
	private decimal? _stopPrice;
	private MoomooSessions _session;

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
	/// US market trading session.
	/// </summary>
	[DataMember]
	public MoomooSessions Session
	{
		get => _session;
		set
		{
			_session = value;
			Parameters[nameof(Session)] = value;
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
/// Moomoo US market trading sessions.
/// </summary>
public enum MoomooSessions
{
	/// <summary>
	/// Regular trading hours.
	/// </summary>
	Regular,

	/// <summary>
	/// Pre-market, regular, and after-hours sessions.
	/// </summary>
	Extended,

	/// <summary>
	/// All sessions including overnight trading.
	/// </summary>
	All,

	/// <summary>
	/// Overnight session only.
	/// </summary>
	Overnight,
}
