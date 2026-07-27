namespace StockSharp.ChoiceFinX;

/// <summary>
/// Choice FinX order-specific parameters.
/// </summary>
[DataContract]
[Serializable]
public sealed class ChoiceFinXOrderCondition : OrderCondition
{
    /// <summary>
    /// Product used for the order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ProductKey,
        Description = LocalizedStrings.ChoiceFinXDeliveryOrIntradayProductDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public ChoiceFinXProducts? Product
    {
        get => (ChoiceFinXProducts?)Parameters.TryGetValue(
            nameof(Product));
        set => Parameters[nameof(Product)] = value;
    }

    /// <summary>
    /// Stop trigger price.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerPriceKey,
        Description = LocalizedStrings.TriggerPriceForAStopLimitOrStopMarketOrderDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Disclosed order volume.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.DisclosedVolumeKey,
        Description = LocalizedStrings.QuantityDisclosedToTheExchangeDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public decimal? DisclosedVolume
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(DisclosedVolume));
        set => Parameters[nameof(DisclosedVolume)] = value;
    }

    /// <summary>
    /// Submit the order outside the regular market session.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AfterMarketKey,
        Description = LocalizedStrings.SubmitTheOrderAsAnAfterMarketOrderDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public bool IsAfterMarket
    {
        get => (bool?)Parameters.TryGetValue(
            nameof(IsAfterMarket)) ?? false;
        set => Parameters[nameof(IsAfterMarket)] = value;
    }

    /// <summary>
    /// Request EDIS authorization for a non-POA account.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.EdisRequiredKey,
        Description = LocalizedStrings.RequestEdisAuthorizationWhenTheAccountHasNoPowerOfAttorneyDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public bool IsEdisRequired
    {
        get => (bool?)Parameters.TryGetValue(
            nameof(IsEdisRequired)) ?? false;
        set => Parameters[nameof(IsEdisRequired)] = value;
    }

    /// <summary>
    /// Free-form order remark.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.RemarksKey,
        Description = LocalizedStrings.FreeFormRemarkSentWithTheOrderDescKey,
        GroupName = LocalizedStrings.OrderKey)]
    public string Remarks
    {
        get => (string)Parameters.TryGetValue(
            nameof(Remarks));
        set => Parameters[nameof(Remarks)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new ChoiceFinXOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
