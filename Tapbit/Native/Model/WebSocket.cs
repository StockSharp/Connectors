namespace StockSharp.Tapbit.Native.Model;

sealed class TapbitWsCommand
{
    [JsonProperty("op")]
    public TapbitWsOperations Operation { get; init; }

    [JsonProperty("args")]
    public string[] Arguments { get; init; }
}

sealed class TapbitWsHeader
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("action")]
    public TapbitWsActions? Action { get; init; }

    [JsonProperty("event")]
    public TapbitWsOperations? Operation { get; init; }

    [JsonProperty("code")]
    public int? Code { get; init; }

    [JsonProperty("message")]
    public string Message { get; init; }
}

sealed class TapbitWsEnvelope<TData>
{
    [JsonProperty("topic")]
    public string Topic { get; init; }

    [JsonProperty("action")]
    public TapbitWsActions? Action { get; init; }

    [JsonProperty("data")]
    public TData[] Data { get; init; }
}

sealed class TapbitWsBook
{
    [JsonProperty("bids")]
    public TapbitLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public TapbitLevel[] Asks { get; init; }

    [JsonProperty("version")]
    public long Version { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class TapbitWsTicker
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("lastPrice")]
    public string LastPrice { get; init; }

    [JsonProperty("markPrice")]
    public string MarkPrice { get; init; }

    [JsonProperty("indexPrice")]
    public string IndexPrice { get; init; }

    [JsonProperty("bestAskPrice")]
    public string BestAskPrice { get; init; }

    [JsonProperty("bestBidPrice")]
    public string BestBidPrice { get; init; }

    [JsonProperty("bestAskVolume")]
    public string BestAskVolume { get; init; }

    [JsonProperty("bestBidVolume")]
    public string BestBidVolume { get; init; }

    [JsonProperty("high24h")]
    public string HighPrice { get; init; }

    [JsonProperty("low24h")]
    public string LowPrice { get; init; }

    [JsonProperty("open24h")]
    public string OpenPrice24h { get; init; }

    [JsonProperty("openPrice")]
    public string OpenPrice { get; init; }

    [JsonProperty("volume24h")]
    public string Volume { get; init; }

    [JsonProperty("fundingRate")]
    public string FundingRate { get; init; }

    [JsonProperty("openInterest")]
    public string OpenInterest { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}
