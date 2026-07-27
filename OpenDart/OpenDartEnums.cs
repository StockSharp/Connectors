namespace StockSharp.OpenDart;

/// <summary>
/// Open DART periodic-report types.
/// </summary>
[DataContract]
public enum OpenDartReportTypes
{
    /// <summary>Annual report.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AnnualReportKey)]
    Annual,

    /// <summary>First-quarter report.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FirstQuarterReportKey)]
    FirstQuarter,

    /// <summary>Semi-annual report.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SemiAnnualReportKey)]
    SemiAnnual,

    /// <summary>Third-quarter report.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ThirdQuarterReportKey)]
    ThirdQuarter,
}

/// <summary>
/// Top-level disclosure filters supported by Open DART.
/// </summary>
[DataContract]
public enum OpenDartDisclosureTypes
{
    /// <summary>All disclosure types.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllDisclosuresKey)]
    All,

    /// <summary>Periodic disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PeriodicDisclosuresKey)]
    Periodic,

    /// <summary>Reports on major issues.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MajorIssuesKey)]
    MajorIssues,

    /// <summary>Issuance disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IssuanceDisclosuresKey)]
    Issuance,

    /// <summary>Equity disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EquityDisclosuresKey)]
    Equity,

    /// <summary>Other disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OtherDisclosuresKey)]
    Other,

    /// <summary>External-audit disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExternalAuditsKey)]
    ExternalAudits,

    /// <summary>Fund disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FundDisclosuresKey)]
    Funds,

    /// <summary>Asset-backed-securitization disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AssetBackedSecuritizationKey)]
    AssetBackedSecuritization,

    /// <summary>Exchange disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ExchangeDisclosuresKey)]
    Exchange,

    /// <summary>Fair Trade Commission disclosures.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FairTradeCommissionKey)]
    FairTradeCommission,
}

/// <summary>
/// Open DART corporation-class filter.
/// </summary>
[DataContract]
public enum OpenDartCorporationClasses
{
    /// <summary>All corporation classes.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllCorporationsKey)]
    All,

    /// <summary>KOSPI corporations.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KospiKey)]
    Kospi,

    /// <summary>KOSDAQ corporations.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KosdaqKey)]
    Kosdaq,

    /// <summary>KONEX corporations.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.KonexKey)]
    Konex,

    /// <summary>Other corporations.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OtherKey)]
    Other,
}
