namespace StockSharp.Edinet;

/// <summary>
/// EDINET document download formats.
/// </summary>
[DataContract]
public enum EdinetDocumentFormats
{
    /// <summary>Filing body, audit reports, and XBRL files in ZIP format.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FilingAndXbrlKey)]
    Filing = 1,

    /// <summary>Filed document in PDF format.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PdfKey)]
    Pdf = 2,

    /// <summary>Attachments in ZIP format.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AttachmentsKey)]
    Attachments = 3,

    /// <summary>English-language filing files in ZIP format.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.EnglishFilesKey)]
    English = 4,

    /// <summary>XBRL converted to CSV files in ZIP format.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CSVKey)]
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
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AllDisclosuresKey)]
    All,

    /// <summary>Annual reports and their corrections.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AnnualReportsKey)]
    AnnualReports,

    /// <summary>Quarterly reports and their corrections.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.QuarterlyReportsKey)]
    QuarterlyReports,

    /// <summary>Semi-annual reports and their corrections.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SemiAnnualReportsKey)]
    SemiAnnualReports,

    /// <summary>Current reports and their corrections.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CurrentReportsKey)]
    CurrentReports,

    /// <summary>Large-shareholding reports and their corrections.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LargeShareholdingReportsKey)]
    LargeShareholdings,
}
