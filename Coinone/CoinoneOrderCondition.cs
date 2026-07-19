namespace StockSharp.Coinone;

/// <summary>
/// Coinone stop-limit and market-order parameters.
/// </summary>
[Serializable]
[DataContract]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CoinoneKey)]
public class CoinoneOrderCondition : OrderCondition, IStopLossOrderCondition
{
    /// <summary>
    /// Stop-limit activation price.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TriggerKey,
        Description = LocalizedStrings.TriggerFieldKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 0)]
    public decimal? TriggerPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
        set => Parameters[nameof(TriggerPrice)] = value;
    }

    /// <summary>
    /// Quote-currency amount for a market buy order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AmountKey,
        Description = LocalizedStrings.AmountKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 1)]
    public decimal? QuoteAmount
    {
        get => (decimal?)Parameters.TryGetValue(nameof(QuoteAmount));
        set => Parameters[nameof(QuoteAmount)] = value;
    }

    /// <summary>
    /// Maximum buy price or minimum sell price for a market order.
    /// </summary>
    [DataMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PriceKey,
        Description = LocalizedStrings.PriceKey,
        GroupName = LocalizedStrings.ParametersKey, Order = 2)]
    public decimal? LimitPrice
    {
        get => (decimal?)Parameters.TryGetValue(nameof(LimitPrice));
        set => Parameters[nameof(LimitPrice)] = value;
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
                    "Coinone does not support trailing-stop orders.");
        }
    }

    /// <inheritdoc />
    public override OrderCondition Clone()
    {
        var clone = new CoinoneOrderCondition();
        clone.Parameters.AddRange(Parameters);
        return clone;
    }
}
