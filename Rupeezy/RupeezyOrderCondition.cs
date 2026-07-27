namespace StockSharp.Rupeezy;

/// <summary>
/// Rupeezy-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class RupeezyOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _orderIdentifier = "OrderIdentifier";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public RupeezyProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<RupeezyProducts?>();
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
    /// Whether the order is submitted after market hours.
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
    /// Optional client identifier echoed by Rupeezy.
    /// </summary>
    [DataMember]
    public string OrderIdentifier
    {
        get => Parameters.TryGetValue(_orderIdentifier)?.ToString();
        set => Parameters[_orderIdentifier] = value;
    }
}
