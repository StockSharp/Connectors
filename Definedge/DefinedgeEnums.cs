namespace StockSharp.Definedge;

/// <summary>
/// Definedge order products.
/// </summary>
[DataContract]
[Serializable]
public enum DefinedgeProducts
{
    /// <summary>
    /// Cash and carry delivery.
    /// </summary>
    [EnumMember]
    Delivery,

    /// <summary>
    /// Intraday product.
    /// </summary>
    [EnumMember]
    Intraday,

    /// <summary>
    /// Carry-forward derivative product.
    /// </summary>
    [EnumMember]
    Normal,
}
