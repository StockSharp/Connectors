namespace StockSharp.Gemini;

/// <summary>
/// Gemini stop-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GeminiKey)]
public class GeminiOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>
	/// Stop activation price. A null value creates a regular order.
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
