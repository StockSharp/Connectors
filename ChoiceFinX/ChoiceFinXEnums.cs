namespace StockSharp.ChoiceFinX;

/// <summary>
/// Choice FinX order products.
/// </summary>
[DataContract]
public enum ChoiceFinXProducts
{
    /// <summary>
    /// Delivery or carry-forward product.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DeliveryKey)]
    Delivery,

    /// <summary>
    /// Margin or intraday product.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntradayKey)]
    Intraday,
}
