namespace StockSharp.Nuvama;

/// <summary>Nuvama order products.</summary>
[DataContract]
[Serializable]
public enum NuvamaProducts
{
    /// <summary>Cash and carry delivery.</summary>
    [EnumMember]
    Cnc,

    /// <summary>Intraday margin.</summary>
    [EnumMember]
    Mis,

    /// <summary>Normal carry-forward derivatives.</summary>
    [EnumMember]
    Nrml,

    /// <summary>Margin trading facility.</summary>
    [EnumMember]
    Mtf,
}
