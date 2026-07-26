namespace StockSharp.Rupeezy;

/// <summary>
/// Rupeezy order products.
/// </summary>
[DataContract]
[Serializable]
public enum RupeezyProducts
{
    /// <summary>
    /// Intraday product.
    /// </summary>
    [EnumMember]
    Intraday,

    /// <summary>
    /// Delivery product.
    /// </summary>
    [EnumMember]
    Delivery,

    /// <summary>
    /// Buy-today-sell-tomorrow product.
    /// </summary>
    [EnumMember]
    Btst,

    /// <summary>
    /// Margin trading facility.
    /// </summary>
    [EnumMember]
    Mtf,
}
