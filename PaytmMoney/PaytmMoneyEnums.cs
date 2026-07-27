namespace StockSharp.PaytmMoney;

/// <summary>
/// Paytm Money order products.
/// </summary>
[DataContract]
public enum PaytmMoneyProducts
{
    /// <summary>
    /// Intraday product.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntradayKey)]
    Intraday,

    /// <summary>
    /// Cash-and-carry delivery product.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DeliveryKey)]
    Delivery,

    /// <summary>
    /// Margin product.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarginKey)]
    Margin,

    /// <summary>
    /// Cover order.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CoverKey)]
    Cover,

    /// <summary>
    /// Bracket order.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.BracketKey)]
    Bracket,
}
