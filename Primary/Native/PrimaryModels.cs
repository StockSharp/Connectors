namespace StockSharp.Primary.Native;

sealed class PrimaryInstrumentId
{
    [JsonProperty("marketId")]
    public string MarketId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}

sealed class PrimarySegment
{
    [JsonProperty("marketSegmentId")]
    public string MarketSegmentId { get; set; }

    [JsonProperty("marketId")]
    public string MarketId { get; set; }
}

sealed class PrimaryInstrument
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("instrumentId")]
    public PrimaryInstrumentId InstrumentId { get; set; }

    [JsonProperty("segment")]
    public PrimarySegment Segment { get; set; }

    [JsonProperty("cficode")]
    public string CfiCode { get; set; }

    [JsonProperty("securityDescription")]
    public string SecurityDescription { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("maturityDate")]
    public string MaturityDate { get; set; }

    [JsonProperty("minPriceIncrement")]
    public decimal MinPriceIncrement { get; set; }

    [JsonProperty("minTradeVol")]
    public decimal MinTradeVolume { get; set; }

    [JsonProperty("maxTradeVol")]
    public decimal MaxTradeVolume { get; set; }

    [JsonProperty("contractMultiplier")]
    public decimal ContractMultiplier { get; set; }

    [JsonProperty("roundLot")]
    public decimal RoundLot { get; set; }

    [JsonProperty("priceConvertionFactor")]
    public decimal PriceConversionFactor { get; set; }

    [JsonProperty("orderTypes")]
    public string[] OrderTypes { get; set; }

    [JsonProperty("timesInForce")]
    public string[] TimesInForce { get; set; }
}

sealed class PrimaryPriceSize
{
    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("size")]
    public decimal Size { get; set; }

    [JsonProperty("date")]
    public long Date { get; set; }
}

sealed class PrimaryMarketUpdate
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("instrumentId")]
    public PrimaryInstrumentId InstrumentId { get; set; }

    [JsonProperty("marketData")]
    public JObject MarketData { get; set; }

    [JsonProperty("depth")]
    public int Depth { get; set; }

    [JsonProperty("aggregated")]
    public bool Aggregated { get; set; }
}

sealed class PrimaryTrade
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("servertime")]
    public long ServerTime { get; set; }

    [JsonProperty("datetime")]
    public string DateTime { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("size")]
    public decimal Size { get; set; }
}

sealed class PrimaryOrderReference
{
    [JsonProperty("clientId")]
    public string ClientId { get; set; }

    [JsonProperty("clOrdId")]
    private string ClientOrderId
    {
        set
        {
            if (ClientId.IsEmpty())
                ClientId = value;
        }
    }

    [JsonProperty("proprietary")]
    public string Proprietary { get; set; }
}

sealed class PrimaryAccountId
{
    [JsonProperty("id")]
    public string Id { get; set; }
}

sealed class PrimaryOrder
{
    [JsonProperty("orderId")]
    public string OrderId { get; set; }

    [JsonProperty("clOrdId")]
    public string ClientOrderId { get; set; }

    [JsonProperty("wsClOrdId")]
    public string WebSocketClientOrderId { get; set; }

    [JsonProperty("proprietary")]
    public string Proprietary { get; set; }

    [JsonProperty("execId")]
    public string ExecutionId { get; set; }

    [JsonProperty("accountId")]
    public PrimaryAccountId AccountId { get; set; }

    [JsonProperty("instrumentId")]
    public PrimaryInstrumentId InstrumentId { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("orderQty")]
    public decimal Quantity { get; set; }

    [JsonProperty("ordType")]
    public string OrderType { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("timeInForce")]
    public string TimeInForce { get; set; }

    [JsonProperty("transactTime")]
    public string TransactionTime { get; set; }

    [JsonProperty("avgPx")]
    public decimal AveragePrice { get; set; }

    [JsonProperty("lastPx")]
    public decimal LastPrice { get; set; }

    [JsonProperty("lastQty")]
    public decimal LastQuantity { get; set; }

    [JsonProperty("cumQty")]
    public decimal CumulativeQuantity { get; set; }

    [JsonProperty("leavesQty")]
    public decimal LeavesQuantity { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("text")]
    public string Text { get; set; }
}

sealed class PrimaryOrderUpdate
{
    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("timestamp")]
    public long Timestamp { get; set; }

    [JsonProperty("orderReport")]
    public PrimaryOrder Order { get; set; }
}

sealed class PrimaryPositionInstrument
{
    [JsonProperty("symbolReference")]
    public string SymbolReference { get; set; }

    [JsonProperty("settlType")]
    public string SettlementType { get; set; }
}

sealed class PrimaryPosition
{
    [JsonProperty("instrument")]
    public PrimaryPositionInstrument Instrument { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("tradingSymbol")]
    public string TradingSymbol { get; set; }

    [JsonProperty("buySize")]
    public decimal BuySize { get; set; }

    [JsonProperty("buyPrice")]
    public decimal BuyPrice { get; set; }

    [JsonProperty("sellSize")]
    public decimal SellSize { get; set; }

    [JsonProperty("sellPrice")]
    public decimal SellPrice { get; set; }

    [JsonProperty("totalDailyDiff")]
    public decimal TotalDailyDifference { get; set; }

    [JsonProperty("totalDiff")]
    public decimal TotalDifference { get; set; }
}

sealed class PrimaryCurrencyBalance
{
    [JsonProperty("consumed")]
    public decimal Consumed { get; set; }

    [JsonProperty("available")]
    public decimal Available { get; set; }
}

sealed class PrimaryCurrencyBalances
{
    [JsonProperty("detailedCurrencyBalance")]
    public Dictionary<string, PrimaryCurrencyBalance> Detailed { get; set; }
}

sealed class PrimaryCash
{
    [JsonProperty("totalCash")]
    public decimal Total { get; set; }

    [JsonProperty("detailedCash")]
    public Dictionary<string, decimal> Detailed { get; set; }
}

sealed class PrimaryAvailableToOperate
{
    [JsonProperty("cash")]
    public PrimaryCash Cash { get; set; }

    [JsonProperty("movements")]
    public decimal Movements { get; set; }

    [JsonProperty("pendingMovements")]
    public decimal PendingMovements { get; set; }

    [JsonProperty("total")]
    public decimal Total { get; set; }
}

sealed class PrimaryDetailedAccountReport
{
    [JsonProperty("currencyBalance")]
    public PrimaryCurrencyBalances CurrencyBalance { get; set; }

    [JsonProperty("availableToOperate")]
    public PrimaryAvailableToOperate AvailableToOperate { get; set; }

    [JsonProperty("settlementDate")]
    public long SettlementDate { get; set; }
}

sealed class PrimaryAccountReport
{
    [JsonProperty("accountName")]
    public string AccountName { get; set; }

    [JsonProperty("marketMember")]
    public string MarketMember { get; set; }

    [JsonProperty("marketMemberIdentity")]
    public string MarketMemberIdentity { get; set; }

    [JsonProperty("collateral")]
    public decimal Collateral { get; set; }

    [JsonProperty("margin")]
    public decimal Margin { get; set; }

    [JsonProperty("availableToCollateral")]
    public decimal AvailableToCollateral { get; set; }

    [JsonProperty("detailedAccountReports")]
    public Dictionary<string, PrimaryDetailedAccountReport> Detailed { get; set; }

    [JsonProperty("hasError")]
    public bool HasError { get; set; }

    [JsonProperty("errorDetail")]
    public string ErrorDetail { get; set; }

    [JsonProperty("lastCalculation")]
    public long LastCalculation { get; set; }

    [JsonProperty("portfolio")]
    public decimal Portfolio { get; set; }

    [JsonProperty("ordersMargin")]
    public decimal OrdersMargin { get; set; }

    [JsonProperty("currentCash")]
    public decimal CurrentCash { get; set; }

    [JsonProperty("dailyDiff")]
    public decimal DailyDifference { get; set; }

    [JsonProperty("uncoveredMargin")]
    public decimal UncoveredMargin { get; set; }
}
