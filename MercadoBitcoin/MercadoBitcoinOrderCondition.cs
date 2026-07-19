namespace StockSharp.MercadoBitcoin;

/// <summary>
/// Mercado Bitcoin stop-limit and quote-cost order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.MercadoBitcoinKey)]
public class MercadoBitcoinOrderCondition : OrderCondition,
    IStopLossOrderCondition
{
    /// <summary>
    /// Price that activates a native stop-limit order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.TriggerKey,
        Description = LocalizedStrings.TriggerFieldKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 0)]
    public decimal? StopPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
        set => Parameters[nameof(StopPrice)] = value;
    }

    /// <summary>
    /// Quote-currency amount to spend for a market buy order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings),
        Name = LocalizedStrings.AmountKey,
        Description = LocalizedStrings.AmountKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 1)]
    public decimal? QuoteCost
    {
        get => (decimal?)Parameters.TryGetValue(nameof(QuoteCost));
        set => Parameters[nameof(QuoteCost)] = value;
    }

    decimal? IStopLossOrderCondition.ActivationPrice
    {
        get => StopPrice;
        set => StopPrice = value;
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
                    "Mercado Bitcoin does not document trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new MercadoBitcoinOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
