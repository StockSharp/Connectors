namespace StockSharp.PintuPro;

/// <summary>
/// Pintu Pro order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.PintuProKey)]
public class PintuProOrderCondition : OrderCondition
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
        var clone = new PintuProOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
