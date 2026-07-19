namespace StockSharp.MercadoBitcoin.Native.Model;

sealed class MercadoBitcoinSymbolsRequest
{
    public string[] Symbols { get; init; }
}

sealed class MercadoBitcoinSymbols
{
    [JsonProperty("symbol")]
    public string[] Symbols { get; init; }

    [JsonProperty("description")]
    public string[] Descriptions { get; init; }

    [JsonProperty("currency")]
    public string[] QuoteCurrencies { get; init; }

    [JsonProperty("base-currency")]
    public string[] BaseCurrencies { get; init; }

    [JsonProperty("exchange-listed")]
    public bool[] IsExchangeListed { get; init; }

    [JsonProperty("exchange-traded")]
    public bool[] IsExchangeTraded { get; init; }

    [JsonProperty("minmovement")]
    public decimal[] MinimumMovements { get; init; }

    [JsonProperty("pricescale")]
    public decimal[] PriceScales { get; init; }

    [JsonProperty("type")]
    public string[] Types { get; init; }

    [JsonProperty("timezone")]
    public string[] TimeZones { get; init; }

    [JsonProperty("session-regular")]
    public string[] Sessions { get; init; }

    [JsonProperty("min-price")]
    public decimal[] MinimumPrices { get; init; }

    [JsonProperty("max-price")]
    public decimal[] MaximumPrices { get; init; }

    [JsonProperty("min-volume")]
    public decimal[] MinimumVolumes { get; init; }

    [JsonProperty("max-volume")]
    public decimal[] MaximumVolumes { get; init; }

    [JsonProperty("min-cost")]
    public decimal[] MinimumCosts { get; init; }

    [JsonProperty("max-cost")]
    public decimal[] MaximumCosts { get; init; }

    [JsonProperty("round-lot")]
    public decimal[] RoundLots { get; init; }
}

sealed class MercadoBitcoinTickersRequest
{
    public string[] Symbols { get; init; }
}

sealed class MercadoBitcoinTicker
{
    [JsonProperty("pair")]
    public string Symbol { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("vol")]
    public decimal Volume { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("buy")]
    public decimal Bid { get; init; }

    [JsonProperty("sell")]
    public decimal Ask { get; init; }

    [JsonProperty("open")]
    public decimal Open { get; init; }

    [JsonProperty("date")]
    public long Timestamp { get; init; }
}

sealed class MercadoBitcoinOrderBookRequest
{
    public int Limit { get; init; }
}

sealed class MercadoBitcoinOrderBook
{
    [JsonProperty("asks")]
    public decimal[][] Asks { get; init; }

    [JsonProperty("bids")]
    public decimal[][] Bids { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }
}

sealed class MercadoBitcoinTradesRequest
{
    public long? TradeId { get; init; }
    public long? SinceTradeId { get; init; }
    public long? From { get; init; }
    public long? To { get; init; }
    public int? Limit { get; init; }
}

sealed class MercadoBitcoinTrade
{
    [JsonProperty("tid")]
    public long TradeId { get; init; }

    [JsonProperty("date")]
    public long Timestamp { get; init; }

    [JsonProperty("type")]
    public MercadoBitcoinOrderSides Side { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("amount")]
    public decimal Volume { get; init; }
}

sealed class MercadoBitcoinCandlesRequest
{
    public string Symbol { get; init; }
    public string Resolution { get; init; }
    public long? From { get; init; }
    public long To { get; init; }
    public int? CountBack { get; init; }
}

sealed class MercadoBitcoinCandles
{
    [JsonProperty("t")]
    public long[] OpenTimes { get; init; }

    [JsonProperty("o")]
    public decimal[] OpenPrices { get; init; }

    [JsonProperty("h")]
    public decimal[] HighPrices { get; init; }

    [JsonProperty("l")]
    public decimal[] LowPrices { get; init; }

    [JsonProperty("c")]
    public decimal[] ClosePrices { get; init; }

    [JsonProperty("v")]
    public decimal[] Volumes { get; init; }
}
