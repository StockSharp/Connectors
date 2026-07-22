namespace StockSharp.IndependentReserve;

/// <summary>
/// Independent Reserve market-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.IndependentReserveKey)]
public class IndependentReserveOrderCondition : OrderCondition
{
	/// <summary>
	/// Whether market-order volume is denominated in the quote currency.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 0)]
	public bool IsVolumeInQuoteCurrency
	{
		get => (bool?)Parameters.TryGetValue(
			nameof(IsVolumeInQuoteCurrency)) ?? false;
		set => Parameters[nameof(IsVolumeInQuoteCurrency)] = value;
	}

	/// <summary>
	/// Maximum allowed market movement, in percent.
	/// </summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		Description = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.ParametersKey,
		Order = 1)]
	public decimal? AllowedSlippagePercent
	{
		get => (decimal?)Parameters.TryGetValue(
			nameof(AllowedSlippagePercent));
		set => Parameters[nameof(AllowedSlippagePercent)] = value;
	}

	/// <inheritdoc />
	public override OrderCondition Clone()
	{
		var clone = new IndependentReserveOrderCondition();
		clone.Parameters.AddRange(Parameters);
		return clone;
	}
}
