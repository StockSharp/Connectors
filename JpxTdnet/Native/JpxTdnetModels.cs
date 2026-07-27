namespace StockSharp.JpxTdnet.Native;

abstract class JpxTdnetResponse
{
    [JsonProperty("statusCode")]
    public string StatusCode { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }
}

sealed class JpxTdnetIndexResponse : JpxTdnetResponse
{
    [JsonProperty("count")]
    public int Count { get; set; }

    [JsonProperty("publiclyList")]
    public JpxTdnetDisclosure[] Items { get; set; }

    [JsonIgnore]
    public bool IsPartial => StatusCode == "206";
}

sealed class JpxTdnetDocumentResponse : JpxTdnetResponse
{
    [JsonProperty("responseType")]
    public string ResponseType { get; set; }

    [JsonProperty("fileData")]
    public string FileData { get; set; }

    [JsonProperty("fileUrl")]
    public string FileUrl { get; set; }
}

sealed class JpxTdnetDisclosure
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("disclosedDate")]
    public string DisclosedDate { get; set; }

    [JsonProperty("disclosedTime")]
    public string DisclosedTime { get; set; }

    [JsonProperty("handlingType")]
    public string HandlingType { get; set; }

    [JsonProperty("disclosureNumber")]
    public string DisclosureNumber { get; set; }

    [JsonProperty("modifiedHistory")]
    public string ModifiedHistory { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("disclosureItems")]
    public string[] DisclosureItems { get; set; }

    [JsonProperty("pdfGeneralFlag")]
    public string GeneralPdfFlag { get; set; }

    [JsonProperty("pdfSummaryFlag")]
    public string SummaryPdfFlag { get; set; }

    [JsonProperty("xbrlFlag")]
    public string XbrlFlag { get; set; }
}

sealed class JpxTdnetIndexRequest
{
    [JsonProperty("accessKey")]
    public string AccessKey { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("dateFrom")]
    public string DateFrom { get; set; }

    [JsonProperty("dateTo")]
    public string DateTo { get; set; }

    [JsonProperty("editDelFlag")]
    public string EditDeleteFlag { get; set; }
}

sealed class JpxTdnetDocumentRequest
{
    [JsonProperty("accessKey")]
    public string AccessKey { get; set; }

    [JsonProperty("disclosureNumber")]
    public string DisclosureNumber { get; set; }

    [JsonProperty("fileTypeFlag")]
    public string FileTypeFlag { get; set; }
}
