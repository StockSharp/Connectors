namespace StockSharp.InvertirOnline;

/// <summary>IOL market country.</summary>
[DataContract]
public enum InvertirOnlineCountries
{
    /// <summary>Argentine market.</summary>
    [EnumMember]
    [Display(Name = "Argentina")]
    Argentina,

    /// <summary>United States market.</summary>
    [EnumMember]
    [Display(Name = "United States")]
    UnitedStates,
}

/// <summary>IOL settlement term.</summary>
[DataContract]
public enum InvertirOnlineSettlements
{
    /// <summary>Same-day settlement.</summary>
    [EnumMember]
    T0,

    /// <summary>One-day settlement.</summary>
    [EnumMember]
    T1,

    /// <summary>Two-day settlement.</summary>
    [EnumMember]
    T2,

    /// <summary>Three-day settlement.</summary>
    [EnumMember]
    T3,
}
