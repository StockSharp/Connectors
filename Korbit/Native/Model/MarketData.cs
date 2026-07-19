namespace StockSharp.Korbit.Native.Model;

sealed class KorbitTradingPair
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("status")]
    public KorbitPairStatuses Status { get; set; }
}

sealed class KorbitTickerRequest
{
    public string Symbols { get; init; }
}

sealed class KorbitTicker
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("open")]
    public decimal Open { get; set; }

    [JsonProperty("high")]
    public decimal High { get; set; }

    [JsonProperty("low")]
    public decimal Low { get; set; }

    [JsonProperty("close")]
    public decimal Close { get; set; }

    [JsonProperty("prevClose")]
    public decimal PreviousClose { get; set; }

    [JsonProperty("priceChange")]
    public decimal PriceChange { get; set; }

    [JsonProperty("priceChangePercent")]
    public decimal PriceChangePercent { get; set; }

    [JsonProperty("volume")]
    public decimal Volume { get; set; }

    [JsonProperty("quoteVolume")]
    public decimal QuoteVolume { get; set; }

    [JsonProperty("bestBidPrice")]
    public decimal BestBidPrice { get; set; }

    [JsonProperty("bestAskPrice")]
    public decimal BestAskPrice { get; set; }

    [JsonProperty("lastTradedAt")]
    public long LastTradedAt { get; set; }
}

sealed class KorbitOrderBookRequest
{
    public string Symbol { get; init; }
    public string Level { get; init; }
}

sealed class KorbitOrderBook
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("bids")]
    public KorbitBookLevel[] Bids { get; set; }

    [JsonProperty("asks")]
    public KorbitBookLevel[] Asks { get; set; }
}

sealed class KorbitBookLevel
{
    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("amt")]
    public decimal? Amount { get; set; }
}

sealed class KorbitTradesRequest
{
    public string Symbol { get; init; }
    public int Limit { get; init; }
}

sealed class KorbitPublicTrade
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qty")]
    public decimal Quantity { get; set; }

    [JsonProperty("isBuyerTaker")]
    public bool IsBuyerTaker { get; set; }

    [JsonProperty("tradeId")]
    public long TradeId { get; set; }
}

sealed class KorbitCandlesRequest
{
    public string Symbol { get; init; }
    public string Interval { get; init; }
    public long? Start { get; init; }
    public long? End { get; init; }
    public int Limit { get; init; }
}

sealed class KorbitCandle
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("open")]
    public decimal Open { get; set; }

    [JsonProperty("high")]
    public decimal High { get; set; }

    [JsonProperty("low")]
    public decimal Low { get; set; }

    [JsonProperty("close")]
    public decimal Close { get; set; }

    [JsonProperty("volume")]
    public decimal Volume { get; set; }
}

sealed class KorbitTickSizeRequest
{
    public string Symbol { get; init; }
}

sealed class KorbitTickSizePolicy
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("tickSizePolicy")]
    public KorbitTickSizeTier[] Tiers { get; set; }

    [JsonProperty("orderbookLevels")]
    public decimal[] OrderBookLevels { get; set; }
}

sealed class KorbitTickSizeTier
{
    [JsonProperty("priceGte")]
    public decimal PriceGreaterThanOrEqual { get; set; }

    [JsonProperty("tickSize")]
    public decimal TickSize { get; set; }
}
