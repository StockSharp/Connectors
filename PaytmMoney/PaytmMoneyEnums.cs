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
    [Display(Name = "Intraday")]
    Intraday,

    /// <summary>
    /// Cash-and-carry delivery product.
    /// </summary>
    [EnumMember]
    [Display(Name = "Delivery")]
    Delivery,

    /// <summary>
    /// Margin product.
    /// </summary>
    [EnumMember]
    [Display(Name = "Margin")]
    Margin,

    /// <summary>
    /// Cover order.
    /// </summary>
    [EnumMember]
    [Display(Name = "Cover")]
    Cover,

    /// <summary>
    /// Bracket order.
    /// </summary>
    [EnumMember]
    [Display(Name = "Bracket")]
    Bracket,
}
