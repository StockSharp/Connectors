namespace StockSharp.TossSecurities;

/// <summary>
/// Toss Securities conditional-order relationships.
/// </summary>
[DataContract]
[Serializable]
public enum TossConditionalOrderTypes
{
    /// <summary>One watched condition.</summary>
    [EnumMember]
    Single,

    /// <summary>One-cancels-the-other conditions.</summary>
    [EnumMember]
    Oco,

    /// <summary>One-triggers-the-other conditions.</summary>
    [EnumMember]
    Oto,
}
