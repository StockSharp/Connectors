namespace StockSharp.PaytmMoney;

/// <summary>
/// Paytm Money order-specific parameters.
/// </summary>
[DataContract]
[Serializable]
public sealed class PaytmMoneyOrderCondition : OrderCondition
{
    /// <summary>
    /// Product used for the order.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Product",
        Description = "Paytm Money order product.",
        GroupName = LocalizedStrings.OrderKey)]
    public PaytmMoneyProducts? Product
    {
        get => (PaytmMoneyProducts?)Parameters.TryGetValue(
            nameof(Product));
        set => Parameters[nameof(Product)] = value;
    }

    /// <summary>
    /// Stop trigger price.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Trigger price",
        Description = "Trigger price for SL or SLM orders.",
        GroupName = LocalizedStrings.OrderKey)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Submit the order outside the regular market session.
    /// </summary>
    [DataMember]
    [Display(
        Name = "After market",
        Description = "Submit the order as an after-market order.",
        GroupName = LocalizedStrings.OrderKey)]
    public bool AfterMarket
    {
        get => (bool?)Parameters.TryGetValue(
            nameof(AfterMarket)) ?? false;
        set => Parameters[nameof(AfterMarket)] = value;
    }

    /// <summary>
    /// Profit distance for a bracket order.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Bracket profit",
        Description = "Profit value for a bracket order.",
        GroupName = LocalizedStrings.OrderKey)]
    public decimal? ProfitValue
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(ProfitValue));
        set => Parameters[nameof(ProfitValue)] = value;
    }

    /// <summary>
    /// Stop-loss distance for a bracket order.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Bracket stop loss",
        Description = "Stop-loss value for a bracket order.",
        GroupName = LocalizedStrings.OrderKey)]
    public decimal? StopLossValue
    {
        get => (decimal?)Parameters.TryGetValue(
            nameof(StopLossValue));
        set => Parameters[nameof(StopLossValue)] = value;
    }

    /// <summary>
    /// Native order leg number used by cover and bracket orders.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Leg number",
        Description = "Native leg number for cover and bracket orders.",
        GroupName = LocalizedStrings.OrderKey)]
    public string LegNumber
    {
        get => (string)Parameters.TryGetValue(
            nameof(LegNumber));
        set => Parameters[nameof(LegNumber)] = value;
    }

    /// <summary>
    /// Native algorithm order number used by bracket orders.
    /// </summary>
    [DataMember]
    [Display(
        Name = "Algorithm order number",
        Description = "Native algorithm order number for a bracket order.",
        GroupName = LocalizedStrings.OrderKey)]
    public string AlgoOrderNumber
    {
        get => (string)Parameters.TryGetValue(
            nameof(AlgoOrderNumber));
        set => Parameters[nameof(AlgoOrderNumber)] = value;
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new PaytmMoneyOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
