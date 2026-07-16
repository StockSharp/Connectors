namespace StockSharp.TigerBrokers;

/// <summary>Tiger Brokers specific order condition.</summary>
[DataContract]
[Serializable]
public class TigerBrokersOrderCondition : OrderCondition
{
	private const string _stopPrice = "StopPrice";
	private const string _trailingPercent = "TrailingPercent";
	private const string _outsideRegularTradingHours = "OutsideRegularTradingHours";
	private const string _session = "Session";
	private const string _userMark = "UserMark";

	/// <summary>Stop activation price.</summary>
	[DataMember]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(_stopPrice)?.To<decimal?>();
		set => Parameters[_stopPrice] = value;
	}

	/// <summary>Trailing distance in percent.</summary>
	[DataMember]
	public decimal? TrailingPercent
	{
		get => Parameters.TryGetValue(_trailingPercent)?.To<decimal?>();
		set => Parameters[_trailingPercent] = value;
	}

	/// <summary>Whether the order may execute outside regular trading hours.</summary>
	[DataMember]
	public bool OutsideRegularTradingHours
	{
		get => Parameters.TryGetValue(_outsideRegularTradingHours)?.To<bool>() == true;
		set => Parameters[_outsideRegularTradingHours] = value;
	}

	/// <summary>Trading session.</summary>
	[DataMember]
	public TigerSessions Session
	{
		get => Parameters.TryGetValue(_session)?.To<TigerSessions>() ?? TigerSessions.Regular;
		set => Parameters[_session] = value;
	}

	/// <summary>Client-defined order mark returned by Tiger.</summary>
	[DataMember]
	public string UserMark
	{
		get => Parameters.TryGetValue(_userMark)?.ToString();
		set => Parameters[_userMark] = value;
	}
}
