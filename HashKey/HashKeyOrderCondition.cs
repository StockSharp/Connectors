namespace StockSharp.HashKey;

/// <summary>
/// HashKey Global order condition.
/// </summary>
[DataContract]
[Serializable]
public sealed class HashKeyOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Stop trigger price for a futures conditional order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Self-trade prevention expires the maker order instead of the taker order.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ModeKey,
		Description = LocalizedStrings.ModeKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public bool IsExpireMaker
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsExpireMaker)) ?? false;
		set => Parameters[nameof(IsExpireMaker)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new HashKeyOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
