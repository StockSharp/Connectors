namespace StockSharp.ProBit;

/// <summary>
/// ProBit order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.ProBitKey)]
public class ProBitOrderCondition : OrderCondition
{
	/// <summary>
	/// Quote-currency amount to spend for a market buy order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}
}
