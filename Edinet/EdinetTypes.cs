namespace StockSharp.Edinet;

/// <summary>
/// EDINET document download formats.
/// </summary>
[DataContract]
public enum EdinetDocumentFormats
{
    /// <summary>Filing body, audit reports, and XBRL files in ZIP format.</summary>
    [EnumMember]
    [Display(Name = "Filing and XBRL")]
    Filing = 1,

    /// <summary>Filed document in PDF format.</summary>
    [EnumMember]
    [Display(Name = "PDF")]
    Pdf = 2,

    /// <summary>Attachments in ZIP format.</summary>
    [EnumMember]
    [Display(Name = "Attachments")]
    Attachments = 3,

    /// <summary>English-language filing files in ZIP format.</summary>
    [EnumMember]
    [Display(Name = "English files")]
    English = 4,

    /// <summary>XBRL converted to CSV files in ZIP format.</summary>
    [EnumMember]
    [Display(Name = "CSV")]
    Csv = 5,
}

/// <summary>
/// Common EDINET disclosure filters.
/// </summary>
[DataContract]
public enum EdinetDisclosureTypes
{
    /// <summary>All disclosure types.</summary>
    [EnumMember]
    [Display(Name = "All disclosures")]
    All,

    /// <summary>Annual reports and their corrections.</summary>
    [EnumMember]
    [Display(Name = "Annual reports")]
    AnnualReports,

    /// <summary>Quarterly reports and their corrections.</summary>
    [EnumMember]
    [Display(Name = "Quarterly reports")]
    QuarterlyReports,

    /// <summary>Semi-annual reports and their corrections.</summary>
    [EnumMember]
    [Display(Name = "Semi-annual reports")]
    SemiAnnualReports,

    /// <summary>Current reports and their corrections.</summary>
    [EnumMember]
    [Display(Name = "Current reports")]
    CurrentReports,

    /// <summary>Large-shareholding reports and their corrections.</summary>
    [EnumMember]
    [Display(Name = "Large-shareholding reports")]
    LargeShareholdings,
}
