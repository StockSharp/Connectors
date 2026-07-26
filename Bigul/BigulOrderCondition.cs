namespace StockSharp.Bigul;

/// <summary>
/// Bigul-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class BigulOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _marketProtection = "MarketProtection";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _remarks = "Remarks";
    private const string _userTag = "UserTag";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public BigulProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<BigulProducts?>();
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
    /// Market-protection value. Zero disables protection.
    /// </summary>
    [DataMember]
    public decimal? MarketProtection
    {
        get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
        set => Parameters[_marketProtection] = value;
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
    /// Client remarks echoed by Bigul.
    /// </summary>
    [DataMember]
    public string Remarks
    {
        get => Parameters.TryGetValue(_remarks)?.ToString();
        set => Parameters[_remarks] = value;
    }

    /// <summary>
    /// Optional client tag.
    /// </summary>
    [DataMember]
    public string UserTag
    {
        get => Parameters.TryGetValue(_userTag)?.ToString();
        set => Parameters[_userTag] = value;
    }
}
