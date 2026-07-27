namespace StockSharp.Definedge;

/// <summary>
/// Definedge-specific order condition.
/// </summary>
[DataContract]
[Serializable]
public class DefinedgeOrderCondition : OrderCondition
{
    private const string _product = "Product";
    private const string _triggerPrice = "TriggerPrice";
    private const string _disclosedVolume = "DisclosedVolume";
    private const string _isAfterMarket = "IsAfterMarket";
    private const string _bookLossPrice = "BookLossPrice";
    private const string _bookProfitPrice = "BookProfitPrice";
    private const string _trailingPrice = "TrailingPrice";
    private const string _marketProtection = "MarketProtection";
    private const string _remarks = "Remarks";

    /// <summary>
    /// Order product.
    /// </summary>
    [DataMember]
    public DefinedgeProducts? Product
    {
        get => Parameters.TryGetValue(_product)?.To<DefinedgeProducts?>();
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
    /// Quantity disclosed to the market.
    /// </summary>
    [DataMember]
    public decimal? DisclosedVolume
    {
        get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
        set => Parameters[_disclosedVolume] = value;
    }

    /// <summary>
    /// Whether the order is placed after regular market hours.
    /// </summary>
    [DataMember]
    public bool IsAfterMarket
    {
        get => Parameters.TryGetValue(_isAfterMarket)?.To<bool>() == true;
        set => Parameters[_isAfterMarket] = value;
    }

    /// <summary>
    /// Absolute stop-loss booking price.
    /// </summary>
    [DataMember]
    public decimal? BookLossPrice
    {
        get => Parameters.TryGetValue(_bookLossPrice)?.To<decimal?>();
        set => Parameters[_bookLossPrice] = value;
    }

    /// <summary>
    /// Absolute profit booking price.
    /// </summary>
    [DataMember]
    public decimal? BookProfitPrice
    {
        get => Parameters.TryGetValue(_bookProfitPrice)?.To<decimal?>();
        set => Parameters[_bookProfitPrice] = value;
    }

    /// <summary>
    /// Trailing stop value.
    /// </summary>
    [DataMember]
    public decimal? TrailingPrice
    {
        get => Parameters.TryGetValue(_trailingPrice)?.To<decimal?>();
        set => Parameters[_trailingPrice] = value;
    }

    /// <summary>
    /// Market protection percentage.
    /// </summary>
    [DataMember]
    public decimal? MarketProtection
    {
        get => Parameters.TryGetValue(_marketProtection)?.To<decimal?>();
        set => Parameters[_marketProtection] = value;
    }

    /// <summary>
    /// Client remarks echoed by Definedge.
    /// </summary>
    [DataMember]
    public string Remarks
    {
        get => Parameters.TryGetValue(_remarks)?.ToString();
        set => Parameters[_remarks] = value;
    }
}
