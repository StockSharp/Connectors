namespace StockSharp.Jainam;

/// <summary>Jainam-specific order condition.</summary>
[DataContract]
[Serializable]
public class JainamOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _complexity = "Complexity";
    private const string _triggerPrice = "TriggerPrice";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _trailingStopLoss = "TrailingStopLoss";
    private const string _marketProtectionPercent = "MarketProtectionPercent";
    private const string _algoId = "AlgoId";
    private const string _orderTag = "OrderTag";

    /// <summary>Order product.</summary>
    [DataMember]
    public JainamProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<JainamProducts?>();
        set => Parameters[_product] = value;
    }

    /// <summary>Order complexity.</summary>
    [DataMember]
    public JainamOrderComplexities? Complexity
    {
        get => Parameters.TryGetValue(_complexity)?.To<JainamOrderComplexities?>();
        set => Parameters[_complexity] = value;
    }

    /// <summary>Stop-order trigger price.</summary>
    [DataMember]
    public decimal? TriggerPrice
    {
        get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
        set => Parameters[_triggerPrice] = value;
    }

    /// <summary>Quantity disclosed to the market.</summary>
    [DataMember]
    public decimal? DisclosedVolume
    {
        get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
        set => Parameters[_disclosedVolume] = value;
    }

    /// <summary>Trailing stop-loss amount.</summary>
    [DataMember]
    public decimal? TrailingStopLoss
    {
        get => Parameters.TryGetValue(_trailingStopLoss)?.To<decimal?>();
        set => Parameters[_trailingStopLoss] = value;
    }

    /// <summary>Market-protection percentage.</summary>
    [DataMember]
    public decimal? MarketProtectionPercent
    {
        get => Parameters.TryGetValue(_marketProtectionPercent)?.To<decimal?>();
        set => Parameters[_marketProtectionPercent] = value;
    }

    /// <summary>Exchange-registered algorithm identifier.</summary>
    [DataMember]
    public string AlgoId
    {
        get => Parameters.TryGetValue(_algoId)?.ToString();
        set => Parameters[_algoId] = value;
    }

    /// <summary>User-defined order tag.</summary>
    [DataMember]
    public string OrderTag
    {
        get => Parameters.TryGetValue(_orderTag)?.ToString();
        set => Parameters[_orderTag] = value;
    }
}
