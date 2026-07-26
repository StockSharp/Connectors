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
        Name = "Product",
        Description = "Choice FinX delivery or intraday product.",
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
        Name = "Trigger price",
        Description = "Trigger price for a stop-limit or stop-market order.",
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
        Name = "Disclosed volume",
        Description = "Quantity disclosed to the exchange.",
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
        Name = "After market",
        Description = "Submit the order as an after-market order.",
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
        Name = "EDIS required",
        Description = "Request EDIS authorization when the account has no power of attorney.",
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
        Name = "Remarks",
        Description = "Free-form remark sent with the order.",
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
