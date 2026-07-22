namespace StockSharp.ChainlinkDataStreams.Native.Model;

sealed class ChainlinkFeed
{
    [JsonProperty("feedID")]
    public string FeedId { get; set; }
}

sealed class ChainlinkFeedsResponse
{
    [JsonProperty("feeds")]
    public ChainlinkFeed[] Feeds { get; set; }
}

sealed class ChainlinkReportEnvelope
{
    [JsonProperty("feedID")]
    public string FeedId { get; set; }

    [JsonProperty("fullReport")]
    public string FullReport { get; set; }

    [JsonProperty("validFromTimestamp")]
    public long ValidFromTimestamp { get; set; }

    [JsonProperty("observationsTimestamp")]
    public long ObservationsTimestamp { get; set; }

    [JsonProperty("validFromTimestampMs")]
    public long ValidFromTimestampMilliseconds { get; set; }

    [JsonProperty("observationsTimestampMs")]
    public long ObservationsTimestampMilliseconds { get; set; }
}

sealed class ChainlinkSingleReportResponse
{
    [JsonProperty("report")]
    public ChainlinkReportEnvelope Report { get; set; }
}

sealed class ChainlinkReportsResponse
{
    [JsonProperty("reports")]
    public ChainlinkReportEnvelope[] Reports { get; set; }
}

sealed class ChainlinkStreamMessage
{
    [JsonProperty("report")]
    public ChainlinkReportEnvelope Report { get; set; }
}

sealed class ChainlinkErrorResponse
{
    [JsonProperty("error")]
    public string Error { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("detail")]
    public string Detail { get; set; }
}

sealed class ChainlinkFeedInfo
{
    public string FeedId { get; init; }
    public ChainlinkReportSchemas Schema { get; init; }
    public ChainlinkTimestampResolutions Resolution { get; init; }
}

sealed class ChainlinkDecodedReport
{
    public string FeedId { get; init; }
    public ChainlinkReportSchemas Schema { get; init; }
    public DateTime ObservationTime { get; init; }
    public DateTime? ValidFromTime { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public DateTime? ValueTime { get; init; }
    public decimal? PrimaryPrice { get; init; }
    public decimal? LastTradePrice { get; init; }
    public decimal? BestBidPrice { get; init; }
    public decimal? BestBidVolume { get; init; }
    public decimal? BestAskPrice { get; init; }
    public decimal? BestAskVolume { get; init; }
    public ChainlinkMarketStatuses MarketStatus { get; init; }
    public bool? IsRipcord { get; init; }
    public string UpdateKey { get; init; }
}
