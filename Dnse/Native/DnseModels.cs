namespace StockSharp.Dnse.Native;

sealed class DnseAccount
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("dealAccount")]
    public bool DealAccount { get; set; }

    [JsonProperty("derivativeAccount")]
    public bool DerivativeAccount { get; set; }
}

sealed class DnseBalance
{
    [JsonProperty("stock")]
    public DnseStockBalance Stock { get; set; }

    [JsonProperty("derivative")]
    public DnseDerivativeBalance Derivative { get; set; }
}

sealed class DnseStockBalance
{
    [JsonProperty("totalCash")]
    public decimal? TotalCash { get; set; }

    [JsonProperty("availableCash")]
    public decimal? AvailableCash { get; set; }

    [JsonProperty("totalDebt")]
    public decimal? TotalDebt { get; set; }

    [JsonProperty("secureAmount")]
    public decimal? SecureAmount { get; set; }

    [JsonProperty("orderSecured")]
    public decimal? OrderSecured { get; set; }

    [JsonProperty("withdrawableCash")]
    public decimal? WithdrawableCash { get; set; }
}

sealed class DnseDerivativeBalance
{
    [JsonProperty("remainSecure")]
    public decimal? RemainSecure { get; set; }

    [JsonProperty("usedSecure")]
    public decimal? UsedSecure { get; set; }

    [JsonProperty("pendingSecure")]
    public decimal? PendingSecure { get; set; }

    [JsonProperty("totalLoanDebt")]
    public decimal? TotalLoanDebt { get; set; }
}

sealed class DnseInstrumentPage
{
    [JsonProperty("data")]
    public DnseInstrument[] Data { get; set; }

    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("page")]
    public int Page { get; set; }

    [JsonProperty("pageSize")]
    public int PageSize { get; set; }
}

sealed class DnseInstrument
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("marketId")]
    public string MarketId { get; set; }

    [JsonProperty("securityGroupId")]
    public string SecurityGroupId { get; set; }

    [JsonProperty("symbolType")]
    public string SymbolType { get; set; }

    [JsonProperty("listedDate")]
    public string ListedDate { get; set; }

    [JsonProperty("shortName")]
    public string ShortName { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("indexName")]
    public string[] IndexNames { get; set; }
}

sealed class DnseSecurityDefinition
{
    [JsonProperty("marketId")]
    public string MarketId { get; set; }

    [JsonProperty("boardId")]
    public string BoardId { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("productGrpId")]
    public string ProductGroupId { get; set; }

    [JsonProperty("securityGroupId")]
    public string SecurityGroupId { get; set; }

    [JsonProperty("basicPrice")]
    public decimal? BasicPrice { get; set; }

    [JsonProperty("ceilingPrice")]
    public decimal? CeilingPrice { get; set; }

    [JsonProperty("floorPrice")]
    public decimal? FloorPrice { get; set; }

    [JsonProperty("securityStatus")]
    public string SecurityStatus { get; set; }

    [JsonProperty("listingDate")]
    public string ListingDate { get; set; }

    [JsonProperty("time")]
    public JToken Time { get; set; }
}

sealed class DnsePriceLevel
{
    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("qtty")]
    public decimal Quantity { get; set; }

    [JsonProperty("quantity")]
    private decimal RestQuantity
    {
        set
        {
            if (Quantity == 0)
                Quantity = value;
        }
    }
}

sealed class DnseQuote
{
    [JsonProperty("marketId")]
    public string MarketId { get; set; }

    [JsonProperty("boardId")]
    public string BoardId { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("bid")]
    public DnsePriceLevel[] Bids { get; set; }

    [JsonProperty("offer")]
    public DnsePriceLevel[] Offers { get; set; }

    [JsonProperty("totalOfferQtty")]
    public decimal? TotalOfferQuantity { get; set; }

    [JsonProperty("totalBidQtty")]
    public decimal? TotalBidQuantity { get; set; }

    [JsonProperty("time")]
    public JToken Time { get; set; }
}

sealed class DnseTrade
{
    [JsonProperty("marketId")]
    public string MarketId { get; set; }

    [JsonProperty("boardId")]
    public string BoardId { get; set; }

    [JsonProperty("isin")]
    public string Isin { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("matchPrice")]
    public decimal MatchPrice { get; set; }

    [JsonProperty("matchQtty")]
    public decimal MatchQuantity { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("avgPrice")]
    public decimal? AveragePrice { get; set; }

    [JsonProperty("totalVolumeTraded")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("grossTradeAmount")]
    public decimal? GrossAmount { get; set; }

    [JsonProperty("highestPrice")]
    public decimal? HighPrice { get; set; }

    [JsonProperty("lowestPrice")]
    public decimal? LowPrice { get; set; }

    [JsonProperty("openPrice")]
    public decimal? OpenPrice { get; set; }

    [JsonProperty("tradingSessionId")]
    public int? TradingSessionId { get; set; }

    [JsonProperty("time")]
    public JToken Time { get; set; }
}

sealed class DnseCandlePage
{
    [JsonProperty("t")]
    public long[] Times { get; set; }

    [JsonProperty("o")]
    public decimal[] Opens { get; set; }

    [JsonProperty("h")]
    public decimal[] Highs { get; set; }

    [JsonProperty("l")]
    public decimal[] Lows { get; set; }

    [JsonProperty("c")]
    public decimal[] Closes { get; set; }

    [JsonProperty("v")]
    public decimal[] Volumes { get; set; }

    [JsonProperty("nextTime")]
    public long NextTime { get; set; }
}

sealed class DnseCandle
{
    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("resolution")]
    public string Resolution { get; set; }

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

    [JsonProperty("time")]
    public JToken Time { get; set; }

    [JsonProperty("lastUpdated")]
    public JToken LastUpdated { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }
}

sealed class DnseOrder
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("accountNo")]
    public string AccountNo { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("priceSecure")]
    public decimal? PriceSecure { get; set; }

    [JsonProperty("averagePrice")]
    public decimal? AveragePrice { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("fillQuantity")]
    public decimal FillQuantity { get; set; }

    [JsonProperty("lastQuantity")]
    public decimal LastQuantity { get; set; }

    [JsonProperty("lastPrice")]
    public decimal? LastPrice { get; set; }

    [JsonProperty("canceledQuantity")]
    public decimal CanceledQuantity { get; set; }

    [JsonProperty("leaveQuantity")]
    public decimal LeaveQuantity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("orderCategory")]
    public string OrderCategory { get; set; }

    [JsonProperty("orderStatus")]
    public string OrderStatus { get; set; }

    [JsonProperty("loanPackageId")]
    public int LoanPackageId { get; set; }

    [JsonProperty("marketType")]
    public string MarketType { get; set; }

    [JsonProperty("transDate")]
    public string TransactionDate { get; set; }

    [JsonProperty("createdDate")]
    public string CreatedDate { get; set; }

    [JsonProperty("modifiedDate")]
    public string ModifiedDate { get; set; }

    [JsonProperty("feeRate")]
    public decimal? FeeRate { get; set; }

    [JsonProperty("error")]
    public string Error { get; set; }

    [JsonProperty("reports")]
    public DnseOrder[] Reports { get; set; }
}

sealed class DnsePosition
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("marketType")]
    public string MarketType { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("accountNo")]
    public string AccountNo { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("loanPackageId")]
    public int LoanPackageId { get; set; }

    [JsonProperty("side")]
    public string Side { get; set; }

    [JsonProperty("accumulateQuantity")]
    public decimal AccumulateQuantity { get; set; }

    [JsonProperty("tradeQuantity")]
    public decimal TradeQuantity { get; set; }

    [JsonProperty("closedQuantity")]
    public decimal ClosedQuantity { get; set; }

    [JsonProperty("openQuantity")]
    public decimal OpenQuantity { get; set; }

    [JsonProperty("overNightQuantity")]
    public decimal OvernightQuantity { get; set; }

    [JsonProperty("costPrice")]
    public decimal? CostPrice { get; set; }

    [JsonProperty("marketPrice")]
    public decimal? MarketPrice { get; set; }

    [JsonProperty("breakEvenPrice")]
    public decimal? BreakEvenPrice { get; set; }

    [JsonProperty("averageClosePrice")]
    public decimal? AverageClosePrice { get; set; }

    [JsonProperty("createdDate")]
    public string CreatedDate { get; set; }

    [JsonProperty("modifiedDate")]
    public string ModifiedDate { get; set; }
}

sealed class DnseAccountUpdate
{
    [JsonProperty("cash")]
    public decimal? Cash { get; set; }

    [JsonProperty("buyingPower")]
    public decimal? BuyingPower { get; set; }

    [JsonProperty("portfolioValue")]
    public decimal? PortfolioValue { get; set; }

    [JsonProperty("equity")]
    public decimal? Equity { get; set; }

    [JsonProperty("timestamp")]
    public JToken Timestamp { get; set; }
}
