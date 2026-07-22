namespace StockSharp.Foxbit;

/// <summary>
/// Foxbit conditional and execution parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.FoxbitKey)]
public class FoxbitOrderCondition : OrderCondition, IStopLossOrderCondition
{
    /// <summary>
    /// Price that activates a stop order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerKey,
        Description = LocalizedStrings.TriggerFieldKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Quote-currency amount for an instant market order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AmountKey,
        Description = LocalizedStrings.AmountKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 1)]
    public decimal? QuoteAmount
    {
        get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
        set => Parameters[nameof(QuoteAmount)] = value;
    }

    /// <summary>
    /// Maximum permitted execution-price deviation expressed as a fraction.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.SlippageKey,
        Description = LocalizedStrings.SlippageKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 2)]
    public decimal? SlippageTolerance
    {
        get => (decimal?)Parameters.TryGetValue(nameof(SlippageTolerance));
        set => Parameters[nameof(SlippageTolerance)] = value;
    }

    /// <summary>
    /// Prevent the new order from trading against an existing own order.
    /// </summary>
    [DataMember]
    [Display(
        ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.ModeKey,
        Description = LocalizedStrings.ModeKey,
        GroupName = LocalizedStrings.ParametersKey,
        Order = 3)]
    public bool IsSelfTradePrevented
    {
        get => (bool?)Parameters.TryGetValue(
            nameof(IsSelfTradePrevented)) ?? false;
        set => Parameters[nameof(IsSelfTradePrevented)] = value;
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => TriggerPrice;
        set => TriggerPrice = value;
    }

    decimal? IStopLossOrderCondition.ClosePositionPrice
    {
        get => null;
        set { }
    }

    bool IStopLossOrderCondition.IsTrailing
    {
        get => false;
        set
        {
            if (value)
                throw new NotSupportedException(
                    "Foxbit does not document trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new FoxbitOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
