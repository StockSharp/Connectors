namespace StockSharp.JpxTdnet;

/// <summary>
/// JPX TDnet document formats.
/// </summary>
[DataContract]
public enum JpxTdnetDocumentFormats
{
    /// <summary>Full-text disclosure PDF.</summary>
    [EnumMember]
    [Display(Name = "Full-text PDF")]
    GeneralPdf,

    /// <summary>Summary disclosure PDF.</summary>
    [EnumMember]
    [Display(Name = "Summary PDF")]
    SummaryPdf,

    /// <summary>XBRL-related ZIP archive.</summary>
    [EnumMember]
    [Display(Name = "XBRL")]
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
    [Display(Name = "Current disclosures")]
    Current,

    /// <summary>
    /// Revision and deletion histories. The API excludes disclosures
    /// that have never been revised or deleted in this mode.
    /// </summary>
    [EnumMember]
    [Display(Name = "Revision and deletion history")]
    RevisionAndDeletionHistory,
}
