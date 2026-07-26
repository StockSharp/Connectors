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
    [Display(Name = "Delivery")]
    Delivery,

    /// <summary>
    /// Margin or intraday product.
    /// </summary>
    [EnumMember]
    [Display(Name = "Intraday")]
    Intraday,
}
