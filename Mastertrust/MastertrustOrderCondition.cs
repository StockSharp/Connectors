namespace StockSharp.Mastertrust;

/// <summary>
/// Mastertrust-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class MastertrustOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _marketProtection = "MarketProtectionPercentage";
    private const string _userOrderId = "UserOrderId";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public MastertrustProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<MastertrustProducts?>();
        set => Parameters[_product] = value;
    }

    /// <summary>
    /// Stop-order trigger price.
    /// </summary>
    [DataMember]
    public decimal? TriggerPrice
    {
        get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
        set => Parameters[_triggerPrice] = value;
    }

    /// <summary>
    /// Whether the order is submitted as an after-market order.
    /// </summary>
    [DataMember]
    public bool IsAfterMarket
    {
        get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
        set => Parameters[_isAfterMarket] = value;
    }

    /// <summary>
    /// Quantity disclosed to the exchange.
    /// </summary>
    [DataMember]
    public decimal? DisclosedVolume
    {
        get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
        set => Parameters[_disclosedVolume] = value;
    }

    /// <summary>
    /// Market-order protection percentage.
    /// </summary>
    [DataMember]
    public decimal? MarketProtectionPercentage
    {
        get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
        set => Parameters[_marketProtection] = value;
    }

    /// <summary>
    /// Optional client order identifier echoed by Mastertrust.
    /// </summary>
    [DataMember]
    public string UserOrderId
    {
        get => Parameters.TryGetValue(_userOrderId)?.ToString();
        set => Parameters[_userOrderId] = value;
    }
}
