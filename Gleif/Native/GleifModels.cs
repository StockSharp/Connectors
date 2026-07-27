namespace StockSharp.Gleif.Native;

sealed class GleifDocument<T>
{
    [JsonProperty("meta")]
    public GleifMeta Meta { get; set; }

    [JsonProperty("links")]
    public GleifLinks Links { get; set; }

    [JsonProperty("data")]
    public T[] Data { get; set; } = [];

    [JsonProperty("errors")]
    public GleifError[] Errors { get; set; } = [];
}

sealed class GleifMeta
{
    [JsonProperty("goldenCopy")]
    public GleifGoldenCopy GoldenCopy { get; set; }

    [JsonProperty("pagination")]
    public GleifPagination Pagination { get; set; }
}

sealed class GleifGoldenCopy
{
    [JsonProperty("publishDate")]
    public DateTimeOffset? PublishDate { get; set; }
}

sealed class GleifPagination
{
    [JsonProperty("currentPage")]
    public int CurrentPage { get; set; }

    [JsonProperty("perPage")]
    public int PerPage { get; set; }

    [JsonProperty("total")]
    public long Total { get; set; }

    [JsonProperty("lastPage")]
    public int LastPage { get; set; }
}

sealed class GleifLinks
{
    [JsonProperty("first")]
    public string First { get; set; }

    [JsonProperty("next")]
    public string Next { get; set; }

    [JsonProperty("last")]
    public string Last { get; set; }
}

sealed class GleifError
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("title")]
    public string Title { get; set; }

    [JsonProperty("detail")]
    public string Detail { get; set; }
}

sealed class GleifLeiRecord
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("attributes")]
    public GleifLeiAttributes Attributes { get; set; }
}

sealed class GleifLeiAttributes
{
    [JsonProperty("lei")]
    public string Lei { get; set; }

    [JsonProperty("entity")]
    public GleifEntity Entity { get; set; }

    [JsonProperty("registration")]
    public GleifRegistration Registration { get; set; }

    [JsonProperty("bic")]
    public string[] Bic { get; set; } = [];

    [JsonProperty("mic")]
    public string[] Mic { get; set; } = [];

    [JsonProperty("conformityFlag")]
    public string ConformityFlag { get; set; }
}

sealed class GleifEntity
{
    [JsonProperty("legalName")]
    public GleifName LegalName { get; set; }

    [JsonProperty("otherNames")]
    public GleifName[] OtherNames { get; set; } = [];

    [JsonProperty("legalAddress")]
    public GleifAddress LegalAddress { get; set; }

    [JsonProperty("headquartersAddress")]
    public GleifAddress HeadquartersAddress { get; set; }

    [JsonProperty("jurisdiction")]
    public string Jurisdiction { get; set; }

    [JsonProperty("category")]
    public string Category { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("creationDate")]
    public DateTimeOffset? CreationDate { get; set; }
}

sealed class GleifName
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("language")]
    public string Language { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }
}

sealed class GleifAddress
{
    [JsonProperty("addressLines")]
    public string[] AddressLines { get; set; } = [];

    [JsonProperty("city")]
    public string City { get; set; }

    [JsonProperty("region")]
    public string Region { get; set; }

    [JsonProperty("country")]
    public string Country { get; set; }

    [JsonProperty("postalCode")]
    public string PostalCode { get; set; }
}

sealed class GleifRegistration
{
    [JsonProperty("initialRegistrationDate")]
    public DateTimeOffset? InitialRegistrationDate { get; set; }

    [JsonProperty("lastUpdateDate")]
    public DateTimeOffset? LastUpdateDate { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("nextRenewalDate")]
    public DateTimeOffset? NextRenewalDate { get; set; }
}

sealed class GleifIsin
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("attributes")]
    public GleifIsinAttributes Attributes { get; set; }
}

sealed class GleifIsinAttributes
{
    [JsonProperty("lei")]
    public string Lei { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }
}

sealed class GleifApiException : InvalidOperationException
{
    public GleifApiException(
        HttpStatusCode? statusCode,
        string apiStatus,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiStatus = apiStatus;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ApiStatus { get; }
}
