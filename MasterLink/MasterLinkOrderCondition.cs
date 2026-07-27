namespace StockSharp.MasterLink;

/// <summary>Taishin Nova API specific order settings.</summary>
[DataContract]
[Serializable]
public class MasterLinkOrderCondition : OrderCondition
{
    private const string _marketType = "MarketType";
    private const string _priceType = "PriceType";
    private const string _orderType = "OrderType";

    /// <summary>Taiwan trading session.</summary>
    [DataMember]
    public MasterLinkMarketTypes MarketType
    {
        get => Parameters.TryGetValue(_marketType)?.To<MasterLinkMarketTypes?>() ??
            MasterLinkMarketTypes.Auto;
        set => Parameters[_marketType] = value;
    }

    /// <summary>Native price flag.</summary>
    [DataMember]
    public MasterLinkPriceTypes PriceType
    {
        get => Parameters.TryGetValue(_priceType)?.To<MasterLinkPriceTypes?>() ??
            MasterLinkPriceTypes.Auto;
        set => Parameters[_priceType] = value;
    }

    /// <summary>Cash, margin, short, or day-trade order type.</summary>
    [DataMember]
    public MasterLinkOrderTypes OrderType
    {
        get => Parameters.TryGetValue(_orderType)?.To<MasterLinkOrderTypes?>() ??
            MasterLinkOrderTypes.Stock;
        set => Parameters[_orderType] = value;
    }
}
