namespace StockSharp.Korbit;

/// <summary>
/// Korbit order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.KorbitKey)]
public class KorbitOrderCondition : OrderCondition
{
    /// <summary>
    /// Quote-currency amount for a market or best-price buy order.
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

    /// <summary>
    /// Use Korbit's best bid/offer order type.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AtBestPriceKey,
        Description = LocalizedStrings.AtBestPriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public bool IsBest
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsBest)) == true;
        set => Parameters[nameof(IsBest)] = value;
    }

    /// <summary>
    /// Price level used by a best bid/offer order, from 1 through 5.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DepthKey,
        Description = LocalizedStrings.DepthKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public int? BestLevel
    {
        get => (int?)Parameters.TryGetValue(nameof(BestLevel));
        set => Parameters[nameof(BestLevel)] = value;
    }

    /// <summary>
    /// Enable Korbit price protection for taker execution.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.PriceKey,
        Description = LocalizedStrings.PriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public bool IsPriceProtection
    {
        get => (bool?)Parameters.TryGetValue(nameof(IsPriceProtection)) == true;
        set => Parameters[nameof(IsPriceProtection)] = value;
    }

    /// <summary>
    /// Price-protection threshold in percent, from 1 through 100.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ErrorPercentKey,
        Description = LocalizedStrings.ErrorPercentKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 4)]
    public int? PriceProtectionPercent
    {
        get => (int?)Parameters.TryGetValue(nameof(PriceProtectionPercent));
        set => Parameters[nameof(PriceProtectionPercent)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new KorbitOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
