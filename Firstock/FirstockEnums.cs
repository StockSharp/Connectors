namespace StockSharp.Firstock;

/// <summary>
/// Firstock order products.
/// </summary>
[DataContract]
[Serializable]
public enum FirstockProducts
{
    /// <summary>
    /// Delivery or cash-and-carry.
    /// </summary>
    [EnumMember]
    Delivery,

    /// <summary>
    /// Margin or carry-forward.
    /// </summary>
    [EnumMember]
    Margin,

    /// <summary>
    /// Intraday.
    /// </summary>
    [EnumMember]
    Intraday,
}
