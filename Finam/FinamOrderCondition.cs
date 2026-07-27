namespace StockSharp.Finam;

/// <summary>
/// Finam Trade API order parameters.
/// </summary>
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.FinamKey)]
[Serializable]
[DataContract]
public class FinamOrderCondition : OrderCondition
{
	/// <summary>
	/// Native time-in-force value.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TimeInForceKey,
		Description = LocalizedStrings.TimeInForceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 0)]
	[DataMember]
	public FinamTimeInForces TimeInForce
	{
		get => (FinamTimeInForces?)Parameters.TryGetValue(nameof(TimeInForce))
			?? FinamTimeInForces.Day;
		set => Parameters[nameof(TimeInForce)] = value;
	}

	/// <summary>
	/// Stop activation price. When set, a stop or stop-limit order is sent.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 1)]
	[DataMember]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Stop trigger direction.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ConditionKey,
		Description = LocalizedStrings.ConditionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey,
		Order = 2)]
	[DataMember]
	public FinamStopConditions StopCondition
	{
		get => (FinamStopConditions?)Parameters.TryGetValue(nameof(StopCondition))
			?? FinamStopConditions.LastUp;
		set => Parameters[nameof(StopCondition)] = value;
	}
}
