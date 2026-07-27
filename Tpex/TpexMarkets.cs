namespace StockSharp.Tpex;

/// <summary>
/// Taipei Exchange equity markets.
/// </summary>
[DataContract]
public enum TpexMarkets
{
    /// <summary>TPEx Mainboard.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MainboardKey)]
    Mainboard,

    /// <summary>Emerging Stock Board.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EmergingStockBoardKey)]
    Emerging,

    /// <summary>Both TPEx equity markets.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllEquityMarketsKey)]
    All,
}
