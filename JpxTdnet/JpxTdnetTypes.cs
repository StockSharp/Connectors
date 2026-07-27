namespace StockSharp.JpxTdnet;

/// <summary>
/// JPX TDnet document formats.
/// </summary>
[DataContract]
public enum JpxTdnetDocumentFormats
{
    /// <summary>Full-text disclosure PDF.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.FullTextPdfKey)]
    GeneralPdf,

    /// <summary>Summary disclosure PDF.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SummaryPdfKey)]
    SummaryPdf,

    /// <summary>XBRL-related ZIP archive.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.XbrlKey)]
    Xbrl,
}

/// <summary>
/// JPX TDnet index result modes.
/// </summary>
[DataContract]
public enum JpxTdnetIndexModes
{
    /// <summary>Current disclosure state, including unmodified records.</summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CurrentDisclosuresKey)]
    Current,

    /// <summary>
    /// Revision and deletion histories. The API excludes disclosures
    /// that have never been revised or deleted in this mode.
    /// </summary>
    [EnumMember]
    [Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.RevisionAndDeletionHistoryKey)]
    RevisionAndDeletionHistory,
}
