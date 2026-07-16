namespace StockSharp.Saxo;

/// <summary>Saxo specific order condition.</summary>
[DataContract]
[Serializable]
public class SaxoOrderCondition : OrderCondition
{
	private const string _stopPrice = "StopPrice";
	private const string _duration = "Duration";
	private const string _trailingDistance = "TrailingDistance";
	private const string _trailingStep = "TrailingStep";
	private const string _forceOpen = "ForceOpen";
	private const string _manualOrder = "ManualOrder";
	private const string _tradingSession = "TradingSession";
	private const string _externalReference = "ExternalReference";

	/// <summary>Stop activation price.</summary>
	[DataMember]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(_stopPrice)?.To<decimal?>();
		set => Parameters[_stopPrice] = value;
	}

	/// <summary>Native order duration.</summary>
	[DataMember]
	public SaxoOrderDurations Duration
	{
		get => Parameters.TryGetValue(_duration)?.To<SaxoOrderDurations>() ?? SaxoOrderDurations.Day;
		set => Parameters[_duration] = value;
	}

	/// <summary>Trailing stop distance to market.</summary>
	[DataMember]
	public decimal? TrailingDistance
	{
		get => Parameters.TryGetValue(_trailingDistance)?.To<decimal?>();
		set => Parameters[_trailingDistance] = value;
	}

	/// <summary>Trailing stop step.</summary>
	[DataMember]
	public decimal? TrailingStep
	{
		get => Parameters.TryGetValue(_trailingStep)?.To<decimal?>();
		set => Parameters[_trailingStep] = value;
	}

	/// <summary>Whether the resulting position must stay force-open.</summary>
	[DataMember]
	public bool ForceOpen
	{
		get => Parameters.TryGetValue(_forceOpen)?.To<bool>() == true;
		set => Parameters[_forceOpen] = value;
	}

	/// <summary>Whether Saxo should classify the order as manually placed.</summary>
	[DataMember]
	public bool ManualOrder
	{
		get => Parameters.TryGetValue(_manualOrder)?.To<bool>() == true;
		set => Parameters[_manualOrder] = value;
	}

	/// <summary>Exchange sessions in which the order may execute.</summary>
	[DataMember]
	public SaxoTradingSessions TradingSession
	{
		get => Parameters.TryGetValue(_tradingSession)?.To<SaxoTradingSessions>() ?? SaxoTradingSessions.Regular;
		set => Parameters[_tradingSession] = value;
	}

	/// <summary>Client correlation value returned by Saxo.</summary>
	[DataMember]
	public string ExternalReference
	{
		get => Parameters.TryGetValue(_externalReference)?.ToString();
		set => Parameters[_externalReference] = value;
	}
}
