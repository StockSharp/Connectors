namespace StockSharp.Bigul;

/// <summary>
/// Bigul order products.
/// </summary>
[DataContract]
[Serializable]
public enum BigulProducts
{
    /// <summary>
    /// Cash-and-carry delivery.
    /// </summary>
    [EnumMember]
    Delivery,

    /// <summary>
    /// Intraday margin.
    /// </summary>
    [EnumMember]
    Intraday,

    /// <summary>
    /// Normal carry-forward product for derivatives.
    /// </summary>
    [EnumMember]
    Normal,
}
