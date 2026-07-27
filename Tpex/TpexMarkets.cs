namespace StockSharp.Tpex;

/// <summary>
/// Taipei Exchange equity markets.
/// </summary>
[DataContract]
public enum TpexMarkets
{
    /// <summary>TPEx Mainboard.</summary>
    [EnumMember]
    [Display(Name = "Mainboard")]
    Mainboard,

    /// <summary>Emerging Stock Board.</summary>
    [EnumMember]
    [Display(Name = "Emerging Stock Board")]
    Emerging,

    /// <summary>Both TPEx equity markets.</summary>
    [EnumMember]
    [Display(Name = "All equity markets")]
    All,
}
