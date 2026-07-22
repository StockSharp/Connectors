namespace StockSharp.NDAX;

/// <summary>
/// NDAX conditional-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.NDAXKey)]
public class NDAXOrderCondition : OrderCondition
{
    /// <summary>
    /// Stop trigger price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.StopPriceKey,
        Description = LocalizedStrings.StopPriceKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Optional one-cancels-the-other order identifier.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.OrderIdKey,
        Description = LocalizedStrings.OrderIdKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public long? OcoOrderId
    {
        get => (long?)Parameters.TryGetValue(nameof(OcoOrderId));
        set => Parameters[nameof(OcoOrderId)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new NDAXOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
