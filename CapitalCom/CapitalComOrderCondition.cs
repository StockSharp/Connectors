namespace StockSharp.CapitalCom;

/// <summary>Capital.com protective order parameters.</summary>
[DataContract]
[Serializable]
public class CapitalComOrderCondition : OrderCondition
{
	private const string _isGuaranteedStop = "IsGuaranteedStop";
	private const string _isTrailingStop = "IsTrailingStop";
	private const string _stopLevel = "StopLevel";
	private const string _stopDistance = "StopDistance";
	private const string _stopAmount = "StopAmount";
	private const string _profitLevel = "ProfitLevel";
	private const string _profitDistance = "ProfitDistance";
	private const string _profitAmount = "ProfitAmount";

	/// <summary>Whether the stop loss is guaranteed.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComGuaranteedStopKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 0)]
	public bool IsGuaranteedStop
	{
		get => Parameters.TryGetValue(_isGuaranteedStop)?.To<bool?>() ?? false;
		set => Parameters[_isGuaranteedStop] = value;
	}

	/// <summary>Whether the stop loss trails the market.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComTrailingStopKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 1)]
	public bool IsTrailingStop
	{
		get => Parameters.TryGetValue(_isTrailingStop)?.To<bool?>() ?? false;
		set => Parameters[_isTrailingStop] = value;
	}

	/// <summary>Absolute stop-loss level.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComStopLevelKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 2)]
	public decimal? StopLevel
	{
		get => Parameters.TryGetValue(_stopLevel)?.To<decimal?>();
		set => Parameters[_stopLevel] = value;
	}

	/// <summary>Stop-loss distance in instrument points.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComStopDistanceKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 3)]
	public decimal? StopDistance
	{
		get => Parameters.TryGetValue(_stopDistance)?.To<decimal?>();
		set => Parameters[_stopDistance] = value;
	}

	/// <summary>Maximum loss amount.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComStopAmountKey,
		GroupName = LocalizedStrings.StopKey,
		Order = 4)]
	public decimal? StopAmount
	{
		get => Parameters.TryGetValue(_stopAmount)?.To<decimal?>();
		set => Parameters[_stopAmount] = value;
	}

	/// <summary>Absolute take-profit level.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComProfitLevelKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 5)]
	public decimal? ProfitLevel
	{
		get => Parameters.TryGetValue(_profitLevel)?.To<decimal?>();
		set => Parameters[_profitLevel] = value;
	}

	/// <summary>Take-profit distance in instrument points.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComProfitDistanceKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 6)]
	public decimal? ProfitDistance
	{
		get => Parameters.TryGetValue(_profitDistance)?.To<decimal?>();
		set => Parameters[_profitDistance] = value;
	}

	/// <summary>Target profit amount.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CapitalComProfitAmountKey,
		GroupName = LocalizedStrings.TakeProfitKey,
		Order = 7)]
	public decimal? ProfitAmount
	{
		get => Parameters.TryGetValue(_profitAmount)?.To<decimal?>();
		set => Parameters[_profitAmount] = value;
	}
}
