namespace StockSharp.Mastertrust;

/// <summary>
/// Mastertrust order products.
/// </summary>
[DataContract]
[Serializable]
public enum MastertrustProducts
{
    /// <summary>
    /// Normal carry-forward product.
    /// </summary>
    [EnumMember]
    Normal,

    /// <summary>
    /// Intraday product.
    /// </summary>
    [EnumMember]
    Intraday,

    /// <summary>
    /// Cash-and-carry delivery product.
    /// </summary>
    [EnumMember]
    Delivery,
}

enum MastertrustStreamModes
{
    Detailed,
    Depth,
}
