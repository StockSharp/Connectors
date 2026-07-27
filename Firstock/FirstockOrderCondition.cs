namespace StockSharp.Firstock;

/// <summary>
/// Firstock-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class FirstockOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _marketProtection = "MarketProtection";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _remarks = "Remarks";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public FirstockProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<FirstockProducts?>();
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
    /// Market-protection percentage for market and stop-market orders.
    /// </summary>
    [DataMember]
    public decimal? MarketProtection
    {
        get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
        set => Parameters[_marketProtection] = value;
    }

    /// <summary>
    /// Whether to use the Firstock after-market order endpoint.
    /// </summary>
    [DataMember]
    public bool IsAfterMarket
    {
        get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
        set => Parameters[_isAfterMarket] = value;
    }

    /// <summary>
    /// Client remarks echoed by Firstock.
    /// </summary>
    [DataMember]
    public string Remarks
    {
        get => Parameters.TryGetValue(_remarks)?.ToString();
        set => Parameters[_remarks] = value;
    }
}
