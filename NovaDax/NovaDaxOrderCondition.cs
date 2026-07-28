namespace StockSharp.NovaDax;

/// <summary>
/// NovaDAX stop-order comparison operators.
/// </summary>
public enum NovaDaxStopOperators
{
	/// <summary>
	/// Trigger when the market price is greater than or equal.
	/// </summary>
	GreaterOrEqual,

	/// <summary>
	/// Trigger when the market price is less than or equal.
	/// </summary>
	LessOrEqual,
}

/// <summary>
/// NovaDAX order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.NovaDaxKey)]
public class NovaDaxOrderCondition : OrderCondition,
	IStopLossOrderCondition
{
	/// <summary>
	/// Stop activation price.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TriggerKey,
		Description = LocalizedStrings.TriggerFieldKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Stop-price comparison operator.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OperatorKey,
		Description = LocalizedStrings.OperatorKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public NovaDaxStopOperators Operator
	{
		get => (NovaDaxStopOperators?)
			Parameters.TryGetValue(nameof(Operator)) ??
			NovaDaxStopOperators.GreaterOrEqual;
		set => Parameters[nameof(Operator)] = value;
	}

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice
	{
		get => null;
		set { }
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}
