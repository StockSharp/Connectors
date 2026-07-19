namespace StockSharp.BYDFi.Native.Model;

sealed class BYDFiWsHeader
{
    [JsonProperty("e")]
    public string Event { get; init; }
}

sealed class BYDFiWsTicker
{
    [JsonProperty("e")]
    public string Event { get; init; }

    [JsonProperty("E")]
    public long EventTime { get; init; }

    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("c")]
    public string LastPrice { get; init; }

    [JsonProperty("o")]
    public string OpenPrice { get; init; }

    [JsonProperty("h")]
    public string HighPrice { get; init; }

    [JsonProperty("l")]
    public string LowPrice { get; init; }

    [JsonProperty("v")]
    public string Volume { get; init; }
}

sealed class BYDFiWsRealTicker
{
    [JsonProperty("e")]
    public string Event { get; init; }

    [JsonProperty("E")]
    public long EventTime { get; init; }

    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("p")]
    public string LastPrice { get; init; }

    [JsonProperty("m")]
    public string MarkPrice { get; init; }

    [JsonProperty("i")]
    public string IndexPrice { get; init; }
}

sealed class BYDFiWsDepth
{
    [JsonProperty("e")]
    public string Event { get; init; }

    [JsonProperty("E")]
    public long EventTime { get; init; }

    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("b")]
    public BYDFiLevel[] Bids { get; init; }

    [JsonProperty("a")]
    public BYDFiLevel[] Asks { get; init; }
}

sealed class BYDFiWsKline
{
    [JsonProperty("e")]
    public string Event { get; init; }

    [JsonProperty("s")]
    public string Symbol { get; init; }

    [JsonProperty("i")]
    public string Interval { get; init; }

    [JsonProperty("t")]
    public long OpenTime { get; init; }

    [JsonProperty("T")]
    public long CloseTime { get; init; }

    [JsonProperty("o")]
    public string Open { get; init; }

    [JsonProperty("c")]
    public string Close { get; init; }

    [JsonProperty("h")]
    public string High { get; init; }

    [JsonProperty("l")]
    public string Low { get; init; }

    [JsonProperty("v")]
    public string Volume { get; init; }
}
