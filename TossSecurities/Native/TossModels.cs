namespace StockSharp.TossSecurities.Native;

sealed class TossAccount
{
    [JsonProperty("accountNo")]
    public string AccountNo { get; set; }

    [JsonProperty("accountSeq")]
    public long AccountSequence { get; set; }

    [JsonProperty("accountType")]
    public string AccountType { get; set; }
}

sealed class TossStock
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("englishName")]
    public string EnglishName { get; set; }

    [JsonProperty("isinCode")]
    public string Isin { get; set; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("securityType")]
    public string SecurityType { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("listDate")]
    public DateTime? ListDate { get; set; }

    [JsonProperty("delistDate")]
    public DateTime? DelistDate { get; set; }

    [JsonProperty("sharesOutstanding")]
    public string SharesOutstanding { get; set; }

    [JsonProperty("leverageFactor")]
    public string LeverageFactor { get; set; }
}

sealed class TossPrice
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("lastPrice")]
    public string LastPrice { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class TossOrderBook
{
    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("asks")]
    public TossOrderBookEntry[] Asks { get; set; }

    [JsonProperty("bids")]
    public TossOrderBookEntry[] Bids { get; set; }
}

sealed class TossOrderBookEntry
{
    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("volume")]
    public string Volume { get; set; }
}

sealed class TossPublicTrade
{
    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("volume")]
    public string Volume { get; set; }

    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class TossPriceLimits
{
    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("upperLimitPrice")]
    public string UpperLimitPrice { get; set; }

    [JsonProperty("lowerLimitPrice")]
    public string LowerLimitPrice { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class TossCandlePage
{
    [JsonProperty("candles")]
    public TossCandle[] Candles { get; set; }

    [JsonProperty("nextBefore")]
    public DateTimeOffset? NextBefore { get; set; }
}

sealed class TossCandle
{
    [JsonProperty("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    [JsonProperty("openPrice")]
    public string OpenPrice { get; set; }

    [JsonProperty("highPrice")]
    public string HighPrice { get; set; }

    [JsonProperty("lowPrice")]
    public string LowPrice { get; set; }

    [JsonProperty("closePrice")]
    public string ClosePrice { get; set; }

    [JsonProperty("volume")]
    public string Volume { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }
}

sealed class TossHoldings
{
    [JsonProperty("totalPurchaseAmount")]
    public TossCurrencyAmounts TotalPurchaseAmount { get; set; }

    [JsonProperty("marketValue")]
    public TossOverviewMarketValue MarketValue { get; set; }

    [JsonProperty("profitLoss")]
    public TossOverviewProfitLoss ProfitLoss { get; set; }

    [JsonProperty("dailyProfitLoss")]
    public TossOverviewDailyProfitLoss DailyProfitLoss { get; set; }

    [JsonProperty("items")]
    public TossHolding[] Items { get; set; }
}

sealed class TossCurrencyAmounts
{
    [JsonProperty("krw")]
    public string Krw { get; set; }

    [JsonProperty("usd")]
    public string Usd { get; set; }
}

sealed class TossOverviewMarketValue
{
    [JsonProperty("amount")]
    public TossCurrencyAmounts Amount { get; set; }

    [JsonProperty("amountAfterCost")]
    public TossCurrencyAmounts AmountAfterCost { get; set; }
}

sealed class TossOverviewProfitLoss
{
    [JsonProperty("amount")]
    public TossCurrencyAmounts Amount { get; set; }

    [JsonProperty("amountAfterCost")]
    public TossCurrencyAmounts AmountAfterCost { get; set; }

    [JsonProperty("rate")]
    public string Rate { get; set; }

    [JsonProperty("rateAfterCost")]
    public string RateAfterCost { get; set; }
}

sealed class TossOverviewDailyProfitLoss
{
    [JsonProperty("amount")]
    public TossCurrencyAmounts Amount { get; set; }

    [JsonProperty("rate")]
    public string Rate { get; set; }
}

sealed class TossHolding
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("marketCountry")]
    public string MarketCountry { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("lastPrice")]
    public string LastPrice { get; set; }

    [JsonProperty("averagePurchasePrice")]
    public string AveragePurchasePrice { get; set; }

    [JsonProperty("profitLoss")]
    public TossHoldingProfitLoss ProfitLoss { get; set; }

    [JsonProperty("dailyProfitLoss")]
    public TossHoldingDailyProfitLoss DailyProfitLoss { get; set; }
}

sealed class TossHoldingProfitLoss
{
    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("amountAfterCost")]
    public string AmountAfterCost { get; set; }

    [JsonProperty("rate")]
    public string Rate { get; set; }

    [JsonProperty("rateAfterCost")]
    public string RateAfterCost { get; set; }
}

sealed class TossHoldingDailyProfitLoss
{
    [JsonProperty("amount")]
    public string Amount { get; set; }

    [JsonProperty("rate")]
    public string Rate { get; set; }
}

sealed class TossBuyingPower
{
    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("cashBuyingPower")]
    public string CashBuyingPower { get; set; }
}

sealed class TossOrderPage
{
    [JsonProperty("orders")]
    public TossOrder[] Orders { get; set; }

    [JsonProperty("nextCursor")]
    public string NextCursor { get; set; }

    [JsonProperty("hasNext")]
    public bool HasNext { get; set; }
}

sealed class TossOrder
{
    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("timeInForce")]
    public string TimeInForce { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("price")]
    public string Price { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("orderAmount")]
    public string OrderAmount { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("orderedAt")]
    public DateTimeOffset OrderedAt { get; set; }

    [JsonProperty("canceledAt")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonProperty("execution")]
    public TossOrderExecution Execution { get; set; }
}

sealed class TossOrderExecution
{
    [JsonProperty("filledQuantity")]
    public string FilledQuantity { get; set; }

    [JsonProperty("averageFilledPrice")]
    public string AverageFilledPrice { get; set; }

    [JsonProperty("filledAmount")]
    public string FilledAmount { get; set; }

    [JsonProperty("commission")]
    public string Commission { get; set; }

    [JsonProperty("tax")]
    public string Tax { get; set; }

    [JsonProperty("filledAt")]
    public DateTimeOffset? FilledAt { get; set; }

    [JsonProperty("settlementDate")]
    public DateTime? SettlementDate { get; set; }
}

sealed class TossOrderResult
{
    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("conditionalOrderId")]
    public string ConditionalOrderId { get; set; }

    [JsonProperty("clientOrderId")]
    public string ClientOrderId { get; set; }

    public string GetOrderId()
        => OrderId.IsEmpty(ConditionalOrderId);
}

sealed class TossConditionalOrderPage
{
    [JsonProperty("conditionalOrders")]
    public TossConditionalOrder[] Orders { get; set; }

    [JsonProperty("nextCursor")]
    public string NextCursor { get; set; }

    [JsonProperty("hasNext")]
    public bool HasNext { get; set; }
}

sealed class TossConditionalOrder
{
    [JsonProperty("conditionalOrderId")]
    public string OrderId { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("quantity")]
    public string Quantity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("expireDate")]
    public DateTime? ExpireDate { get; set; }

    [JsonProperty("first")]
    public TossConditionalLeg First { get; set; }

    [JsonProperty("second")]
    public TossConditionalLeg Second { get; set; }

    [JsonProperty("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class TossConditionalLeg
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("orderSide")]
    public string OrderSide { get; set; }

    [JsonProperty("triggerPrice")]
    public string TriggerPrice { get; set; }

    [JsonProperty("targetProfitRate")]
    public string TargetProfitRate { get; set; }

    [JsonProperty("orderPrice")]
    public string OrderPrice { get; set; }

    [JsonProperty("triggeredOrderId")]
    public string TriggeredOrderId { get; set; }
}

static class TossExtensions
{
    public static decimal? ToDecimalValue(this string value)
        => decimal.TryParse(value, NumberStyles.Any,
            CultureInfo.InvariantCulture, out var result)
                ? result
                : null;

    public static string ToNative(this decimal value)
        => value.ToString(CultureInfo.InvariantCulture);

    public static CurrencyTypes? ToCurrency(this string value)
        => value?.ToUpperInvariant() switch
        {
            "KRW" => CurrencyTypes.KRW,
            "USD" => CurrencyTypes.USD,
            _ => null,
        };

    public static string ToBoard(this string market, string currency = null)
    {
        if (market.EqualsIgnoreCase("KR"))
            return "KRX";
        if (market.EqualsIgnoreCase("US"))
            return "NASDAQ";
        if (!market.IsEmpty())
            return market.ToUpperInvariant();
        return currency.EqualsIgnoreCase("KRW") ? "KRX" : "NASDAQ";
    }

    public static SecurityTypes ToSecurityType(this string value)
        => value?.ToUpperInvariant() switch
        {
            "DEPOSITARY_RECEIPT" => SecurityTypes.Adr,
            "ETF" or "FOREIGN_ETF" => SecurityTypes.Etf,
            "INFRASTRUCTURE_FUND" or "REIT" => SecurityTypes.Fund,
            "STOCK_WARRANTS" => SecurityTypes.Warrant,
            "ETN" => SecurityTypes.Bond,
            _ => SecurityTypes.Stock,
        };

    public static Sides ToSide(this string value)
        => value.EqualsIgnoreCase("SELL") ? Sides.Sell : Sides.Buy;

    public static string ToNative(this Sides side)
        => side == Sides.Buy ? "BUY" : "SELL";

    public static OrderStates ToOrderState(this string value)
        => value?.ToUpperInvariant() switch
        {
            "PENDING" or "PENDING_CANCEL" or "PENDING_REPLACE" or
                "PARTIAL_FILLED" => OrderStates.Active,
            "FILLED" or "CANCELED" or "REPLACED" => OrderStates.Done,
            "REJECTED" or "CANCEL_REJECTED" or
                "REPLACE_REJECTED" => OrderStates.Failed,
            _ => OrderStates.Pending,
        };

    public static OrderStates ToConditionalOrderState(this string value)
        => value?.ToUpperInvariant() switch
        {
            "WATCHING" or "PAUSED" or "ORDERING" or "ORDERED" =>
                OrderStates.Active,
            "COMPLETED" or "EXPIRED" or "CANCELED" =>
                OrderStates.Done,
            _ => OrderStates.Pending,
        };

    public static TossConditionalOrderTypes ToConditionalType(
        this string value)
        => value?.ToUpperInvariant() switch
        {
            "OCO" => TossConditionalOrderTypes.Oco,
            "OTO" => TossConditionalOrderTypes.Oto,
            _ => TossConditionalOrderTypes.Single,
        };

    public static string ToNative(this TossConditionalOrderTypes value)
        => value switch
        {
            TossConditionalOrderTypes.Oco => "OCO",
            TossConditionalOrderTypes.Oto => "OTO",
            _ => "SINGLE",
        };
}
