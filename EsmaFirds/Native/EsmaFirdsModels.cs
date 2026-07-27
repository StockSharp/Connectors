namespace StockSharp.EsmaFirds.Native;

sealed class EsmaSolrEnvelope<T>
{
    [JsonProperty("responseHeader")]
    public EsmaSolrResponseHeader Header { get; set; }

    [JsonProperty("response")]
    public EsmaSolrResponse<T> Response { get; set; }

    [JsonProperty("error")]
    public EsmaSolrError Error { get; set; }
}

sealed class EsmaSolrResponseHeader
{
    [JsonProperty("status")]
    public int Status { get; set; }

    [JsonProperty("QTime")]
    public long QueryTime { get; set; }
}

sealed class EsmaSolrResponse<T>
{
    [JsonProperty("numFound")]
    public long NumberFound { get; set; }

    [JsonProperty("start")]
    public long Start { get; set; }

    [JsonProperty("numFoundExact")]
    public bool NumberFoundExact { get; set; }

    [JsonProperty("docs")]
    public T[] Documents { get; set; } = [];
}

sealed class EsmaSolrError
{
    [JsonProperty("msg")]
    public string Message { get; set; }

    [JsonProperty("code")]
    public int? Code { get; set; }
}

sealed class EsmaInstrument
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("mic")]
    public string Mic { get; set; }

    [JsonProperty("gnr_full_name")]
    public string FullName { get; set; }

    [JsonProperty("gnr_short_name")]
    public string ShortName { get; set; }

    [JsonProperty("gnr_cfi_code")]
    public string CfiCode { get; set; }

    [JsonProperty("gnr_notional_curr_code")]
    public string Currency { get; set; }

    [JsonProperty("lei")]
    public string Lei { get; set; }

    [JsonProperty("mrkt_issr_trdng_rqst_flag")]
    public string IssuerTradingRequest { get; set; }

    [JsonProperty("mrkt_trdng_start_date")]
    public string TradingStartDate { get; set; }

    [JsonProperty("mrkt_trdng_trmination_date")]
    public string TradingTerminationDate { get; set; }

    [JsonProperty("rca_mic")]
    public string RelevantAuthorityMic { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("status_label")]
    public string StatusLabel { get; set; }

    [JsonProperty("publication_date")]
    public string PublicationDate { get; set; }

    [JsonProperty("latest_received_flag")]
    public string LatestReceivedFlag { get; set; }

    [JsonProperty("never_published_flag")]
    public string NeverPublishedFlag { get; set; }
}

sealed record EsmaInstrumentSearch(
    string Value,
    string Mic,
    string[] CfiCategories,
    bool ActiveOnly,
    int Start,
    int Rows);

sealed class EsmaFirdsApiException : InvalidOperationException
{
    public EsmaFirdsApiException(
        HttpStatusCode? statusCode,
        int? solrCode,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        SolrCode = solrCode;
    }

    public HttpStatusCode? StatusCode { get; }

    public int? SolrCode { get; }
}
