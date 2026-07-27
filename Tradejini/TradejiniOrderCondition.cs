namespace StockSharp.Tradejini;

/// <summary>
/// Tradejini-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class TradejiniOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _validity = "Validity";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _marketProtection = "MarketProtection";
    private const string _remarks = "Remarks";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public TradejiniProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<TradejiniProducts?>();
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
    /// Native Tradejini order validity.
    /// </summary>
    [DataMember]
    public TradejiniValidities? Validity
    {
        get => Parameters.TryGetValue(_validity)?.To<TradejiniValidities?>();
        set => Parameters[_validity] = value;
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
    /// Market-order protection percentage.
    /// </summary>
    [DataMember]
    public decimal? MarketProtection
    {
        get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
        set => Parameters[_marketProtection] = value;
    }

    /// <summary>
    /// Optional order-book tag. Tradejini accepts at most ten characters.
    /// </summary>
    [DataMember]
    public string Remarks
    {
        get => Parameters.TryGetValue(_remarks)?.ToString();
        set => Parameters[_remarks] = value;
    }
}
