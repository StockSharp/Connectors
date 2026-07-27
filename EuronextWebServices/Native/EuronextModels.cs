namespace StockSharp.EuronextWebServices.Native;

sealed class EuronextInstrumentResponse
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("entityID")]
    public string EntityId { get; set; }

    [JsonProperty("view")]
    public string View { get; set; }

    [JsonProperty("sessionQuality")]
    public string SessionQuality { get; set; }

    [JsonProperty("instr")]
    public EuronextInstrument Instrument { get; set; }
}

sealed class EuronextInstrument
{
    [JsonProperty("iid")]
    public string Id { get; set; }

    [JsonProperty("exchCode")]
    public string ExchangeCode { get; set; }

    [JsonProperty("cdStand")]
    public string Code { get; set; }

    [JsonProperty("codifStand")]
    public string Codification { get; set; }

    [JsonProperty("longNm")]
    public string LongName { get; set; }

    [JsonProperty("cfiCode")]
    public string CfiCode { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("issueDt")]
    public string IssueDate { get; set; }

    [JsonProperty("listBeginDt")]
    public string ListingDate { get; set; }

    [JsonProperty("nbShare")]
    public decimal? NumberOfShares { get; set; }

    [JsonProperty("tradLot")]
    public decimal? TradingLot { get; set; }

    [JsonProperty("accuracy")]
    public int? Accuracy { get; set; }

    [JsonProperty("tickSize")]
    public decimal? TickSize { get; set; }

    [JsonProperty("quality")]
    public string Quality { get; set; }

    [JsonProperty("currInstrSess")]
    public EuronextSession CurrentSession { get; set; }

    [JsonProperty("prevInstrSess")]
    public EuronextSession PreviousSession { get; set; }

    [JsonProperty("ordBook")]
    public EuronextOrderBook OrderBook { get; set; }
}

sealed class EuronextSession
{
    [JsonProperty("dateTime")]
    public string DateTime { get; set; }

    [JsonProperty("lastPx")]
    public decimal? LastPrice { get; set; }

    [JsonProperty("lastQty")]
    public decimal? LastQuantity { get; set; }

    [JsonProperty("openPx")]
    public decimal? OpenPrice { get; set; }

    [JsonProperty("closPx")]
    public decimal? ClosePrice { get; set; }

    [JsonProperty("risVarLim")]
    public decimal? HighLimit { get; set; }

    [JsonProperty("falVarLim")]
    public decimal? LowLimit { get; set; }

    [JsonProperty("tradedQty")]
    public decimal? TradedQuantity { get; set; }

    [JsonProperty("nbTrades")]
    public long? TradesCount { get; set; }

    [JsonProperty("lastUpdate")]
    public string LastUpdate { get; set; }

    [JsonProperty("marketCapitalisation")]
    public decimal? MarketCapitalization { get; set; }

    [JsonProperty("prevAdjClosingPrice")]
    public decimal? PreviousClose { get; set; }

    [JsonProperty("vwap")]
    public decimal? Vwap { get; set; }
}

sealed class EuronextOrderBook
{
    [JsonProperty("bsBidPx")]
    public decimal? BestBidPrice { get; set; }

    [JsonProperty("bsBidQty")]
    public decimal? BestBidQuantity { get; set; }

    [JsonProperty("bsBidDtTm")]
    public string BestBidTime { get; set; }

    [JsonProperty("bsAskPx")]
    public decimal? BestAskPrice { get; set; }

    [JsonProperty("bsAskQty")]
    public decimal? BestAskQuantity { get; set; }

    [JsonProperty("bsAskDtTm")]
    public string BestAskTime { get; set; }

    [JsonProperty("ordBkLnAsk")]
    public EuronextBookLevel[] Asks { get; set; } = [];

    [JsonProperty("ordBkLnBid")]
    public EuronextBookLevel[] Bids { get; set; } = [];
}

sealed class EuronextBookLevel
{
    [JsonProperty("qty")]
    public decimal? Quantity { get; set; }

    [JsonProperty("nbOrd")]
    public int? OrdersCount { get; set; }

    [JsonProperty("px")]
    public decimal? Price { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("dateTime")]
    public string DateTime { get; set; }
}

sealed class EuronextIntradayResponse
{
    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("entityID")]
    public string EntityId { get; set; }

    [JsonProperty("view")]
    public string View { get; set; }

    [JsonProperty("sessionQuality")]
    public string SessionQuality { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("accuracy")]
    public int? Accuracy { get; set; }

    [JsonProperty("tickSize")]
    public decimal? TickSize { get; set; }

    [JsonProperty("intradayPoint")]
    public EuronextIntradayPoint[] Points { get; set; } = [];
}

sealed class EuronextIntradayPoint
{
    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("nbTrade")]
    public int? TradesCount { get; set; }

    [JsonProperty("beginPx")]
    public decimal? OpenPrice { get; set; }

    [JsonProperty("beginTime")]
    public string OpenTime { get; set; }

    [JsonProperty("endPX")]
    public decimal? ClosePrice { get; set; }

    [JsonProperty("endTime")]
    public string CloseTime { get; set; }

    [JsonProperty("highPx")]
    public decimal? HighPrice { get; set; }

    [JsonProperty("lowPx")]
    public decimal? LowPrice { get; set; }

    [JsonProperty("vol")]
    public decimal? Volume { get; set; }

    [JsonProperty("amt")]
    public decimal? Turnover { get; set; }

    [JsonProperty("tradeStatus")]
    public string TradeStatus { get; set; }

    [JsonProperty("tradeType")]
    public string TradeType { get; set; }

    [JsonProperty("strProvTrdID")]
    public string TradeId { get; set; }
}

sealed class EuronextWebServicesApiException :
    InvalidOperationException
{
    public EuronextWebServicesApiException(
        HttpStatusCode? statusCode,
        string apiStatus,
        string message)
        : base(message)
    {
        StatusCode = statusCode;
        ApiStatus = apiStatus;
    }

    public HttpStatusCode? StatusCode { get; }

    public string ApiStatus { get; }
}
