namespace StockSharp.Coinigy;

/// <summary>
/// <see cref="Coinigy"/> order condition.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CoinigyKey)]
public class CoinigyOrderCondition : OrderCondition
{
	/// <summary>
	/// Initializes a new instance of the <see cref="CoinigyOrderCondition"/>.
	/// </summary>
	public CoinigyOrderCondition()
	{
	}

	/// <summary>
	/// Activation price, when reached an order will be placed.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.StopLossKey,
		Order = 1)]
	public decimal? ConditionalPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(ConditionalPrice));
		set => Parameters[nameof(ConditionalPrice)] = value;
	}
}