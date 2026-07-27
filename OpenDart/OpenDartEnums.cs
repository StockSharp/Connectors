namespace StockSharp.OpenDart;

/// <summary>
/// Open DART periodic-report types.
/// </summary>
[DataContract]
public enum OpenDartReportTypes
{
    /// <summary>Annual report.</summary>
    [EnumMember]
    [Display(Name = "Annual report")]
    Annual,

    /// <summary>First-quarter report.</summary>
    [EnumMember]
    [Display(Name = "First-quarter report")]
    FirstQuarter,

    /// <summary>Semi-annual report.</summary>
    [EnumMember]
    [Display(Name = "Semi-annual report")]
    SemiAnnual,

    /// <summary>Third-quarter report.</summary>
    [EnumMember]
    [Display(Name = "Third-quarter report")]
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
    [Display(Name = "All disclosures")]
    All,

    /// <summary>Periodic disclosures.</summary>
    [EnumMember]
    [Display(Name = "Periodic disclosures")]
    Periodic,

    /// <summary>Reports on major issues.</summary>
    [EnumMember]
    [Display(Name = "Major issues")]
    MajorIssues,

    /// <summary>Issuance disclosures.</summary>
    [EnumMember]
    [Display(Name = "Issuance disclosures")]
    Issuance,

    /// <summary>Equity disclosures.</summary>
    [EnumMember]
    [Display(Name = "Equity disclosures")]
    Equity,

    /// <summary>Other disclosures.</summary>
    [EnumMember]
    [Display(Name = "Other disclosures")]
    Other,

    /// <summary>External-audit disclosures.</summary>
    [EnumMember]
    [Display(Name = "External audits")]
    ExternalAudits,

    /// <summary>Fund disclosures.</summary>
    [EnumMember]
    [Display(Name = "Fund disclosures")]
    Funds,

    /// <summary>Asset-backed-securitization disclosures.</summary>
    [EnumMember]
    [Display(Name = "Asset-backed securitization")]
    AssetBackedSecuritization,

    /// <summary>Exchange disclosures.</summary>
    [EnumMember]
    [Display(Name = "Exchange disclosures")]
    Exchange,

    /// <summary>Fair Trade Commission disclosures.</summary>
    [EnumMember]
    [Display(Name = "Fair Trade Commission")]
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
    [Display(Name = "All corporations")]
    All,

    /// <summary>KOSPI corporations.</summary>
    [EnumMember]
    [Display(Name = "KOSPI")]
    Kospi,

    /// <summary>KOSDAQ corporations.</summary>
    [EnumMember]
    [Display(Name = "KOSDAQ")]
    Kosdaq,

    /// <summary>KONEX corporations.</summary>
    [EnumMember]
    [Display(Name = "KONEX")]
    Konex,

    /// <summary>Other corporations.</summary>
    [EnumMember]
    [Display(Name = "Other")]
    Other,
}
