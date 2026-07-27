namespace StockSharp.Edinet.Native;

sealed class EdinetListResponse
{
    [JsonProperty("metadata")]
    public EdinetMetadata Metadata { get; set; }

    [JsonProperty("results")]
    public EdinetDocument[] Results { get; set; }

    [JsonProperty("StatusCode")]
    public int? StatusCode { get; set; }

    [JsonProperty("message")]
    public string ErrorMessage { get; set; }
}

sealed class EdinetMetadata
{
    [JsonProperty("resultset")]
    public EdinetResultSet ResultSet { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class EdinetResultSet
{
    [JsonProperty("count")]
    public int Count { get; set; }
}

sealed class EdinetDocument
{
    [JsonProperty("seqNumber")]
    public int SequenceNumber { get; set; }

    [JsonProperty("docID")]
    public string DocumentId { get; set; }

    [JsonProperty("edinetCode")]
    public string EdinetCode { get; set; }

    [JsonProperty("secCode")]
    public string SecuritiesCode { get; set; }

    [JsonProperty("JCN")]
    public string CorporateNumber { get; set; }

    [JsonProperty("filerName")]
    public string FilerName { get; set; }

    [JsonProperty("fundCode")]
    public string FundCode { get; set; }

    [JsonProperty("ordinanceCode")]
    public string OrdinanceCode { get; set; }

    [JsonProperty("formCode")]
    public string FormCode { get; set; }

    [JsonProperty("docTypeCode")]
    public string DocumentTypeCode { get; set; }

    [JsonProperty("periodStart")]
    public string PeriodStart { get; set; }

    [JsonProperty("periodEnd")]
    public string PeriodEnd { get; set; }

    [JsonProperty("submitDateTime")]
    public string SubmittedAt { get; set; }

    [JsonProperty("docDescription")]
    public string Description { get; set; }

    [JsonProperty("issuerEdinetCode")]
    public string IssuerEdinetCode { get; set; }

    [JsonProperty("subjectEdinetCode")]
    public string SubjectEdinetCode { get; set; }

    [JsonProperty("subsidiaryEdinetCode")]
    public string SubsidiaryEdinetCode { get; set; }

    [JsonProperty("currentReportReason")]
    public string CurrentReportReason { get; set; }

    [JsonProperty("parentDocID")]
    public string ParentDocumentId { get; set; }

    [JsonProperty("opeDateTime")]
    public string OperationDateTime { get; set; }

    [JsonProperty("withdrawalStatus")]
    public string WithdrawalStatus { get; set; }

    [JsonProperty("docInfoEditStatus")]
    public string InformationEditStatus { get; set; }

    [JsonProperty("disclosureStatus")]
    public string DisclosureStatus { get; set; }

    [JsonProperty("xbrlFlag")]
    public string XbrlFlag { get; set; }

    [JsonProperty("pdfFlag")]
    public string PdfFlag { get; set; }

    [JsonProperty("attachDocFlag")]
    public string AttachmentFlag { get; set; }

    [JsonProperty("englishDocFlag")]
    public string EnglishFlag { get; set; }

    [JsonProperty("csvFlag")]
    public string CsvFlag { get; set; }

    [JsonProperty("legalStatus")]
    public string LegalStatus { get; set; }
}

sealed class EdinetCompany
{
    public string EdinetCode { get; set; }
    public string SubmitterType { get; set; }
    public string ListingStatus { get; set; }
    public string ConsolidationStatus { get; set; }
    public string CapitalStock { get; set; }
    public string ClosingDate { get; set; }
    public string Name { get; set; }
    public string EnglishName { get; set; }
    public string PhoneticName { get; set; }
    public string Province { get; set; }
    public string Industry { get; set; }
    public string SecuritiesCode { get; set; }
    public string CorporateNumber { get; set; }
}
