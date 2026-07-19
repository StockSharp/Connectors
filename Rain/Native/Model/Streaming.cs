namespace StockSharp.Rain.Native.Model;

enum RainSocketChannels
{
    Trades,
    OrderBook,
    ProductSummary,
    MarketSummary,
    Candles,
    AccountBalance,
    Orders,
}

sealed class RainSocketCommand
{
    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("data")]
    public string Data { get; init; }
}

sealed class RainSocketNameEnvelope
{
    [JsonProperty("name")]
    public string Name { get; init; }
}

sealed class RainSocketEnvelope<TData>
{
    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("data")]
    public TData Data { get; init; }
}

sealed class RainSocketError
{
    [JsonProperty("message")]
    public string Message { get; init; }

    [JsonProperty("reason")]
    public string Reason { get; init; }
}

sealed class RainSocketBook
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("bids")]
    public RainBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public RainBookLevel[] Asks { get; init; }
}

sealed class RainSocketTrades
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("data")]
    public RainPublicTrade[] Trades { get; init; }
}

sealed class RainSocketCandle
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("interval")]
    public string Interval { get; init; }

    [JsonProperty("data")]
    public RainCandle Candle { get; init; }
}

sealed class RainSocketProductSummary
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("bid_price")]
    public RainAmount BidPrice { get; init; }

    [JsonProperty("ask_price")]
    public RainAmount AskPrice { get; init; }

    [JsonProperty("last_price")]
    public RainAmount LastPrice { get; init; }

    [JsonProperty("minimum_sell")]
    public RainAmount MinimumSell { get; init; }

    [JsonProperty("minimum_buy")]
    public RainAmount MinimumBuy { get; init; }
}

sealed class RainSocketMarketSummary
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("percent_change")]
    public decimal? PercentChange { get; init; }

    [JsonProperty("volume")]
    public RainAmount Volume { get; init; }

    [JsonProperty("low")]
    public RainAmount Low { get; init; }

    [JsonProperty("high")]
    public RainAmount High { get; init; }
}

sealed class RainSocketAccounts
{
    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("data")]
    public RainAccount[] Accounts { get; init; }
}

sealed class RainSocketOrders
{
    [JsonProperty("sequence")]
    public long Sequence { get; init; }

    [JsonProperty("orders")]
    public RainOrder[] Orders { get; init; }
}
