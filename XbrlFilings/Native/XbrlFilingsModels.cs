namespace StockSharp.XbrlFilings.Native;

sealed class XbrlJsonApiDocument<T>
{
    [JsonProperty("data")]
    public T[] Data { get; set; } = [];

    [JsonProperty("included")]
    public XbrlEntity[] Included { get; set; } = [];

    [JsonProperty("links")]
    public XbrlJsonApiLinks Links { get; set; }

    [JsonProperty("meta")]
    public XbrlJsonApiMeta Meta { get; set; }

    [JsonProperty("errors")]
    public XbrlJsonApiError[] Errors { get; set; } = [];
}

sealed class XbrlJsonApiLinks
{
    [JsonProperty("self")]
    public string Self { get; set; }

    [JsonProperty("first")]
    public string First { get; set; }

    [JsonProperty("last")]
    public string Last { get; set; }

    [JsonProperty("next")]
    public string Next { get; set; }

    [JsonProperty("prev")]
    public string Previous { get; set; }
}

sealed class XbrlJsonApiMeta
{
    [JsonProperty("count")]
    public long Count { get; set; }
}

sealed class XbrlJsonApiError
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("detail")]
    public string Detail { get; set; }
}

sealed class XbrlEntity
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("attributes")]
    public XbrlEntityAttributes Attributes { get; set; }

    [JsonProperty("links")]
    public XbrlResourceLinks Links { get; set; }
}

sealed class XbrlEntityAttributes
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("identifier")]
    public string Identifier { get; set; }
}

sealed class XbrlFiling
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("attributes")]
    public XbrlFilingAttributes Attributes { get; set; }

    [JsonProperty("relationships")]
    public XbrlFilingRelationships Relationships { get; set; }

    [JsonProperty("links")]
    public XbrlResourceLinks Links { get; set; }
}

sealed class XbrlFilingAttributes
{
    [JsonProperty("processed")]
    public string Processed { get; set; }

    [JsonProperty("viewer_url")]
    public string ViewerUrl { get; set; }

    [JsonProperty("report_url")]
    public string ReportUrl { get; set; }

    [JsonProperty("date_added")]
    public string DateAdded { get; set; }

    [JsonProperty("period_end")]
    public string PeriodEnd { get; set; }

    [JsonProperty("error_count")]
    public int ErrorCount { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("fxo_id")]
    public string FilingId { get; set; }

    [JsonProperty("warning_count")]
    public int WarningCount { get; set; }

    [JsonProperty("json_url")]
    public string JsonUrl { get; set; }

    [JsonProperty("sha256")]
    public string Sha256 { get; set; }

    [JsonProperty("package_url")]
    public string PackageUrl { get; set; }

    [JsonProperty("inconsistency_count")]
    public int InconsistencyCount { get; set; }
}

sealed class XbrlFilingRelationships
{
    [JsonProperty("entity")]
    public XbrlRelationship Entity { get; set; }

    [JsonProperty("validation_messages")]
    public XbrlRelationship ValidationMessages { get; set; }
}

sealed class XbrlRelationship
{
    [JsonProperty("data")]
    public XbrlRelationshipData Data { get; set; }

    [JsonProperty("links")]
    public XbrlRelationshipLinks Links { get; set; }
}

sealed class XbrlRelationshipData
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }
}

sealed class XbrlRelationshipLinks
{
    [JsonProperty("related")]
    public string Related { get; set; }
}

sealed class XbrlResourceLinks
{
    [JsonProperty("self")]
    public string Self { get; set; }
}

sealed class XbrlFilingsApiException : InvalidOperationException
{
    public XbrlFilingsApiException(
        HttpStatusCode? statusCode,
        string apiCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiCode = apiCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ApiCode { get; }
}
