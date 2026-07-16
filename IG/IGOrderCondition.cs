namespace StockSharp.IG;

/// <summary>IG order condition.</summary>
[DataContract]
[Serializable]
public class IgOrderCondition : OrderCondition
{
	private const string _expiry = "Expiry";
	private const string _currencyCode = "CurrencyCode";
	private const string _forceOpen = "ForceOpen";
	private const string _guaranteedStop = "GuaranteedStop";
	private const string _stopLevel = "StopLevel";
	private const string _stopDistance = "StopDistance";
	private const string _limitLevel = "LimitLevel";
	private const string _limitDistance = "LimitDistance";
	private const string _trailingStop = "TrailingStop";
	private const string _trailingStopIncrement = "TrailingStopIncrement";

	/// <summary>Native instrument expiry, for example <c>DFB</c>.</summary>
	[DataMember]
	[Display(Name = "Expiry", GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public string Expiry
	{
		get => Parameters.TryGetValue(_expiry)?.To<string>().IsEmpty("DFB");
		set => Parameters[_expiry] = value;
	}

	/// <summary>Deal currency ISO code.</summary>
	[DataMember]
	[Display(Name = "Currency", GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public string CurrencyCode
	{
		get => Parameters.TryGetValue(_currencyCode)?.To<string>();
		set => Parameters[_currencyCode] = value;
	}

	/// <summary>Create an independent position instead of netting.</summary>
	[DataMember]
	[Display(Name = "Force open", GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public bool ForceOpen
	{
		get => Parameters.TryGetValue(_forceOpen)?.To<bool?>() ?? true;
		set => Parameters[_forceOpen] = value;
	}

	/// <summary>Use a guaranteed stop.</summary>
	[DataMember]
	[Display(Name = "Guaranteed stop", GroupName = LocalizedStrings.StopKey, Order = 3)]
	public bool GuaranteedStop
	{
		get => Parameters.TryGetValue(_guaranteedStop)?.To<bool?>() ?? false;
		set => Parameters[_guaranteedStop] = value;
	}

	/// <summary>Absolute stop level.</summary>
	[DataMember]
	[Display(Name = "Stop level", GroupName = LocalizedStrings.StopKey, Order = 4)]
	public decimal? StopLevel
	{
		get => Parameters.TryGetValue(_stopLevel)?.To<decimal?>();
		set => Parameters[_stopLevel] = value;
	}

	/// <summary>Stop distance in instrument points.</summary>
	[DataMember]
	[Display(Name = "Stop distance", GroupName = LocalizedStrings.StopKey, Order = 5)]
	public decimal? StopDistance
	{
		get => Parameters.TryGetValue(_stopDistance)?.To<decimal?>();
		set => Parameters[_stopDistance] = value;
	}

	/// <summary>Absolute profit-taking level.</summary>
	[DataMember]
	[Display(Name = "Limit level", GroupName = LocalizedStrings.LimitKey, Order = 6)]
	public decimal? LimitLevel
	{
		get => Parameters.TryGetValue(_limitLevel)?.To<decimal?>();
		set => Parameters[_limitLevel] = value;
	}

	/// <summary>Profit-taking distance in instrument points.</summary>
	[DataMember]
	[Display(Name = "Limit distance", GroupName = LocalizedStrings.LimitKey, Order = 7)]
	public decimal? LimitDistance
	{
		get => Parameters.TryGetValue(_limitDistance)?.To<decimal?>();
		set => Parameters[_limitDistance] = value;
	}

	/// <summary>Enable a trailing stop.</summary>
	[DataMember]
	[Display(Name = "Trailing stop", GroupName = LocalizedStrings.StopKey, Order = 8)]
	public bool TrailingStop
	{
		get => Parameters.TryGetValue(_trailingStop)?.To<bool?>() ?? false;
		set => Parameters[_trailingStop] = value;
	}

	/// <summary>Trailing-stop increment in instrument points.</summary>
	[DataMember]
	[Display(Name = "Trailing increment", GroupName = LocalizedStrings.StopKey, Order = 9)]
	public decimal? TrailingStopIncrement
	{
		get => Parameters.TryGetValue(_trailingStopIncrement)?.To<decimal?>();
		set => Parameters[_trailingStopIncrement] = value;
	}
}
