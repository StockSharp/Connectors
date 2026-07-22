namespace StockSharp.Bitkub;

/// <summary>
/// Bitkub order condition.
/// </summary>
[DataContract]
[Serializable]
public sealed class BitkubOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Quote-currency amount to spend for a market buy order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AmountKey,
		Description = LocalizedStrings.AmountKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? QuoteAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
		set => Parameters[nameof(QuoteAmount)] = value;
	}

	/// <summary>
	/// Stop trigger price reported for externally created stop-limit orders.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.ParametersKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new BitkubOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
