namespace StockSharp.GmoCoin.Native.Model;

sealed class GmoCoinStatus
{
    [JsonProperty("status")]
    public GmoCoinServiceStatuses Status { get; init; }
}

sealed class GmoCoinSymbol
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("minOrderSize")]
    public decimal MinimumOrderSize { get; init; }

    [JsonProperty("maxOrderSize")]
    public decimal MaximumOrderSize { get; init; }

    [JsonProperty("sizeStep")]
    public decimal SizeStep { get; init; }

    [JsonProperty("tickSize")]
    public decimal TickSize { get; init; }

    [JsonProperty("takerFee")]
    public decimal TakerFee { get; init; }

    [JsonProperty("makerFee")]
    public decimal MakerFee { get; init; }
}

sealed class GmoCoinTickerRequest
{
    public string Symbol { get; init; }
}

sealed class GmoCoinTicker
{
    [JsonProperty("ask")]
    public decimal Ask { get; init; }

    [JsonProperty("bid")]
    public decimal Bid { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }
}

sealed class GmoCoinOrderBookRequest
{
    public string Symbol { get; init; }
}

sealed class GmoCoinOrderBook
{
    [JsonProperty("asks")]
    public GmoCoinBookLevel[] Asks { get; init; }

    [JsonProperty("bids")]
    public GmoCoinBookLevel[] Bids { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }
}

sealed class GmoCoinBookLevel
{
    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }
}

sealed class GmoCoinTradesRequest
{
    public string Symbol { get; init; }
    public int Page { get; init; }
    public int Count { get; init; }
}

sealed class GmoCoinPage<TItem>
{
    [JsonProperty("pagination")]
    public GmoCoinPagination Pagination { get; init; }

    [JsonProperty("list")]
    public TItem[] Items { get; init; }
}

sealed class GmoCoinList<TItem>
{
    [JsonProperty("list")]
    public TItem[] Items { get; init; }
}

sealed class GmoCoinPagination
{
    [JsonProperty("currentPage")]
    public int CurrentPage { get; init; }

    [JsonProperty("count")]
    public int Count { get; init; }
}

sealed class GmoCoinPublicTrade
{
    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("side")]
    public GmoCoinSides Side { get; init; }

    [JsonProperty("size")]
    public decimal Size { get; init; }

    [JsonProperty("timestamp")]
    public string Timestamp { get; init; }

    [JsonProperty("symbol")]
    public string Symbol { get; init; }
}

sealed class GmoCoinKlinesRequest
{
    public string Symbol { get; init; }
    public string Interval { get; init; }
    public string Date { get; init; }
}

sealed class GmoCoinCandle
{
    [JsonProperty("openTime")]
    public long OpenTime { get; init; }

    [JsonProperty("open")]
    public decimal Open { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("close")]
    public decimal Close { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }
}
