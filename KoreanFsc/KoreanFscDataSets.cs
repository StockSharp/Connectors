namespace StockSharp.KoreanFsc;

/// <summary>
/// Price datasets published by the Korean Financial Services Commission.
/// </summary>
[DataContract]
public enum KoreanFscDataSets
{
    /// <summary>KRX-listed stocks.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StocksKey)]
    Stocks,

    /// <summary>Listed beneficiary and income securities.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IncomeSecuritiesKey)]
    IncomeSecurities,

    /// <summary>Listed preemptive-right securities.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PreemptiveRightSecuritiesKey)]
    PreemptiveRightSecurities,

    /// <summary>Listed preemptive-right certificates.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PreemptiveRightCertificatesKey)]
    PreemptiveRightCertificates,
}

/// <summary>
/// Optional Korean listing-market filter.
/// </summary>
[DataContract]
public enum KoreanFscMarkets
{
    /// <summary>All markets.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllMarketsKey)]
    All,

    /// <summary>KOSPI.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KospiKey)]
    Kospi,

    /// <summary>KOSDAQ.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KosdaqKey)]
    Kosdaq,

    /// <summary>KONEX.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KonexKey)]
    Konex,
}
