namespace StockSharp.Rain;

/// <summary>
/// Rain order execution parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.RainKey)]
public class RainOrderCondition : OrderCondition
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

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new RainOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
