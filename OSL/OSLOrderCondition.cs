namespace StockSharp.OSL;

/// <summary>
/// OSL self-trade prevention policy.
/// </summary>
[DataContract]
public enum OSLSelfTradePrevention
{
    /// <summary>
    /// Cancel the taker order.
    /// </summary>
    [EnumMember]
    ExpireTaker,

    /// <summary>
    /// Cancel the maker order.
    /// </summary>
    [EnumMember]
    ExpireMaker,

    /// <summary>
    /// Cancel both orders.
    /// </summary>
    [EnumMember]
    ExpireBoth,
}

/// <summary>
/// OSL order execution parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.OSLKey)]
public class OSLOrderCondition : OrderCondition
{
    /// <summary>
    /// Quote-currency amount used for a market buy.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AmountKey,
        Description = LocalizedStrings.AmountKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 0)]
    public decimal? QuoteAmount
    {
        get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
        set => Parameters[nameof(QuoteAmount)] = value;
    }

    /// <summary>
    /// Self-trade prevention policy.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ModeKey,
        Description = LocalizedStrings.ModeKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 1)]
    public OSLSelfTradePrevention SelfTradePrevention
    {
        get => (OSLSelfTradePrevention?)Parameters.TryGetValue(
            nameof(SelfTradePrevention)) ??
            OSLSelfTradePrevention.ExpireTaker;
        set => Parameters[nameof(SelfTradePrevention)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new OSLOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
