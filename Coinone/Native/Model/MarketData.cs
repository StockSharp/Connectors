namespace StockSharp.Coinone.Native.Model;

sealed class CoinoneMarketsResponse : CoinoneResponse
{
    [JsonProperty("markets")]
    public CoinoneMarket[] Markets { get; init; }
}

sealed class CoinoneMarket
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("price_unit")]
    public decimal PriceUnit { get; init; }

    [JsonProperty("qty_unit")]
    public decimal QuantityUnit { get; init; }

    [JsonProperty("max_order_amount")]
    public decimal MaximumOrderAmount { get; init; }

    [JsonProperty("max_price")]
    public decimal MaximumPrice { get; init; }

    [JsonProperty("max_qty")]
    public decimal MaximumQuantity { get; init; }

    [JsonProperty("min_order_amount")]
    public decimal MinimumOrderAmount { get; init; }

    [JsonProperty("min_price")]
    public decimal MinimumPrice { get; init; }

    [JsonProperty("min_qty")]
    public decimal MinimumQuantity { get; init; }

    [JsonProperty("order_book_units")]
    public decimal[] OrderBookUnits { get; init; }

    [JsonProperty("maintenance_status")]
    public CoinoneMaintenanceStatuses MaintenanceStatus { get; init; }

    [JsonProperty("trade_status")]
    public CoinoneTradeStatuses TradeStatus { get; init; }

    [JsonProperty("order_types")]
    public CoinoneOrderTypes[] OrderTypes { get; init; }
}

sealed class CoinoneMarketRequest
{
    public string QuoteCurrency { get; init; }
    public string TargetCurrency { get; init; }
}

sealed class CoinoneOrderBookRequest
{
    public string QuoteCurrency { get; init; }
    public string TargetCurrency { get; init; }
    public int Size { get; init; }
    public decimal? OrderBookUnit { get; init; }
}

sealed class CoinoneTradesRequest
{
    public string QuoteCurrency { get; init; }
    public string TargetCurrency { get; init; }
    public int Size { get; init; }
}

sealed class CoinoneChartRequest
{
    public string QuoteCurrency { get; init; }
    public string TargetCurrency { get; init; }
    public string Interval { get; init; }
    public long? Timestamp { get; init; }
    public int Size { get; init; }
}

sealed class CoinoneBookResponse : CoinoneResponse
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("order_book_unit")]
    public decimal OrderBookUnit { get; init; }

    [JsonProperty("bids")]
    public CoinoneBookLevel[] Bids { get; init; }

    [JsonProperty("asks")]
    public CoinoneBookLevel[] Asks { get; init; }
}

sealed class CoinoneBookLevel
{
    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }
}

sealed class CoinoneTickerResponse : CoinoneResponse
{
    [JsonProperty("tickers")]
    public CoinoneTicker[] Tickers { get; init; }
}

sealed class CoinoneTicker
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("first")]
    public decimal Open { get; init; }

    [JsonProperty("last")]
    public decimal Last { get; init; }

    [JsonProperty("quote_volume")]
    public decimal QuoteVolume { get; init; }

    [JsonProperty("target_volume")]
    public decimal TargetVolume { get; init; }

    [JsonProperty("best_asks")]
    public CoinoneBookLevel[] BestAsks { get; init; }

    [JsonProperty("best_bids")]
    public CoinoneBookLevel[] BestBids { get; init; }

    [JsonProperty("id")]
    public string Id { get; init; }
}

sealed class CoinoneTradesResponse : CoinoneResponse
{
    [JsonProperty("quote_currency")]
    public string QuoteCurrency { get; init; }

    [JsonProperty("target_currency")]
    public string TargetCurrency { get; init; }

    [JsonProperty("transactions")]
    public CoinonePublicTrade[] Transactions { get; init; }
}

sealed class CoinonePublicTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("qty")]
    public decimal Quantity { get; init; }

    [JsonProperty("is_seller_maker")]
    public bool IsSellerMaker { get; init; }
}

sealed class CoinoneChartResponse : CoinoneResponse
{
    [JsonProperty("is_last")]
    public bool IsLast { get; init; }

    [JsonProperty("chart")]
    public CoinoneCandle[] Chart { get; init; }
}

sealed class CoinoneCandle
{
    [JsonProperty("timestamp")]
    public long Timestamp { get; init; }

    [JsonProperty("open")]
    public decimal Open { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("close")]
    public decimal Close { get; init; }

    [JsonProperty("target_volume")]
    public decimal TargetVolume { get; init; }

    [JsonProperty("quote_volume")]
    public decimal QuoteVolume { get; init; }
}
