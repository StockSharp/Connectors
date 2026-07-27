namespace StockSharp.KoreanFsc;

/// <summary>
/// Price datasets published by the Korean Financial Services Commission.
/// </summary>
[DataContract]
public enum KoreanFscDataSets
{
    /// <summary>KRX-listed stocks.</summary>
    [EnumMember]
    [Display(Name = "Stocks")]
    Stocks,

    /// <summary>Listed beneficiary and income securities.</summary>
    [EnumMember]
    [Display(Name = "Income securities")]
    IncomeSecurities,

    /// <summary>Listed preemptive-right securities.</summary>
    [EnumMember]
    [Display(Name = "Preemptive-right securities")]
    PreemptiveRightSecurities,

    /// <summary>Listed preemptive-right certificates.</summary>
    [EnumMember]
    [Display(Name = "Preemptive-right certificates")]
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
    [Display(Name = "All markets")]
    All,

    /// <summary>KOSPI.</summary>
    [EnumMember]
    [Display(Name = "KOSPI")]
    Kospi,

    /// <summary>KOSDAQ.</summary>
    [EnumMember]
    [Display(Name = "KOSDAQ")]
    Kosdaq,

    /// <summary>KONEX.</summary>
    [EnumMember]
    [Display(Name = "KONEX")]
    Konex,
}
