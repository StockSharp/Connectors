namespace StockSharp.Indodax;

/// <summary>
/// Indodax order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.IndodaxKey)]
public class IndodaxOrderCondition : OrderCondition
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

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new IndodaxOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
