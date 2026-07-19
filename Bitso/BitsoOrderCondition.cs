namespace StockSharp.Bitso;

/// <summary>
/// Bitso order condition.
/// </summary>
[DataContract]
[Serializable]
public sealed class BitsoOrderCondition : BaseWithdrawOrderCondition
{
	/// <summary>
	/// Stop trigger price.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>
	/// Maximum market-order slippage in percent.
	/// </summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageSizeKey,
		GroupName = LocalizedStrings.GeneralKey)]
	public decimal? SlippageTolerance
	{
		get => (decimal?)Parameters.TryGetValue(nameof(SlippageTolerance));
		set => Parameters[nameof(SlippageTolerance)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new BitsoOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
