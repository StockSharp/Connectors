namespace StockSharp.TaseDataHub.Native;

sealed class TaseOAuthToken
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; }

    [JsonProperty("token_type")]
    public string TokenType { get; set; }

    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
}

sealed class TaseResult<T>
{
    [JsonProperty("result")]
    public T[] Result { get; set; } = [];

    [JsonProperty("total")]
    public int Total { get; set; }
}

sealed class TaseSecuritiesEnvelope
{
    [JsonProperty("tradeSecuritiesList")]
    public TaseResult<TaseSecurity> Securities { get; set; }
}

sealed class TaseSecurityTypesEnvelope
{
    [JsonProperty("securitiesTypes")]
    public TaseResult<TaseSecurityType> SecurityTypes { get; set; }
}

sealed class TaseEodEnvelope
{
    [JsonProperty("securitiesEndOfDayTradingData")]
    public TaseResult<TaseEodRecord> Records { get; set; }
}

sealed class TaseSecurity
{
    [JsonProperty("companyName")]
    public string CompanyName { get; set; }

    [JsonProperty("companySector")]
    public string CompanySector { get; set; }

    [JsonProperty("companySubSector")]
    public string CompanySubSector { get; set; }

    [JsonProperty("companySuperSector")]
    public string CompanySuperSector { get; set; }

    [JsonProperty("corporateId")]
    public string CorporateId { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("issuerId")]
    public long? IssuerId { get; set; }

    [JsonProperty("securityFullTypeCode")]
    public string SecurityFullTypeCode { get; set; }

    [JsonProperty("securityId")]
    public long SecurityId { get; set; }

    [JsonProperty("securityName")]
    public string SecurityName { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}

sealed class TaseSecurityType
{
    [JsonProperty("securityFullTypeCode")]
    public string FullTypeCode { get; set; }

    [JsonProperty("securityMainTypeCode")]
    public string MainTypeCode { get; set; }

    [JsonProperty("securityMainTypeDesc")]
    public string MainTypeDescription { get; set; }

    [JsonProperty("securityTypeDesc")]
    public string TypeDescription { get; set; }
}

sealed class TaseEodRecord
{
    [JsonProperty("adjustedClosingPrice")]
    public decimal? AdjustedClosingPrice { get; set; }

    [JsonProperty("adjustmentCoefficient")]
    public decimal? AdjustmentCoefficient { get; set; }

    [JsonProperty("basePrice")]
    public decimal? BasePrice { get; set; }

    [JsonProperty("change")]
    public string Change { get; set; }

    [JsonProperty("changeValue")]
    public decimal? ChangeValue { get; set; }

    [JsonProperty("closingPrice")]
    public decimal? ClosingPrice { get; set; }

    [JsonProperty("exCode")]
    public decimal? ExCode { get; set; }

    [JsonProperty("firstTradingDate")]
    public string FirstTradingDate { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("listedCapital")]
    public decimal? ListedCapital { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("marketCap")]
    public decimal? MarketCap { get; set; }

    [JsonProperty("marketType")]
    public string MarketType { get; set; }

    [JsonProperty("minContPhaseAmount")]
    public decimal? MinimumContinuousAmount { get; set; }

    [JsonProperty("openingPrice")]
    public decimal? OpeningPrice { get; set; }

    [JsonProperty("securityId")]
    public long SecurityId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("tradeDate")]
    public string TradeDate { get; set; }

    [JsonProperty("transactionsNumber")]
    public long? TransactionsNumber { get; set; }

    [JsonProperty("turnover")]
    public decimal? Turnover { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }
}

sealed class TaseDataHubApiException : InvalidOperationException
{
    public TaseDataHubApiException(
        HttpStatusCode? statusCode,
        string code,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }

    public HttpStatusCode? StatusCode { get; }

    public string Code { get; }
}
