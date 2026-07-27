namespace StockSharp.Jainam;

/// <summary>Jainam order products.</summary>
[DataContract]
[Serializable]
public enum JainamProducts
{
    /// <summary>Delivery or long-term product.</summary>
    [EnumMember]
    LongTerm,

    /// <summary>Intraday product.</summary>
    [EnumMember]
    Intraday,

    /// <summary>Margin trading facility.</summary>
    [EnumMember]
    Mtf,
}

/// <summary>Jainam order complexities.</summary>
[DataContract]
[Serializable]
public enum JainamOrderComplexities
{
    /// <summary>Regular order.</summary>
    [EnumMember]
    Regular,

    /// <summary>After-market order.</summary>
    [EnumMember]
    AfterMarket,
}
