namespace StockSharp.Tpex.Native;

sealed class TpexMainboardRow
{
    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("SecuritiesCompanyCode")]
    public string Code { get; set; }

    [JsonProperty("CompanyName")]
    public string Name { get; set; }

    [JsonProperty("Close")]
    public string Close { get; set; }

    [JsonProperty("Change")]
    public string Change { get; set; }

    [JsonProperty("Open")]
    public string Open { get; set; }

    [JsonProperty("High")]
    public string High { get; set; }

    [JsonProperty("Low")]
    public string Low { get; set; }

    [JsonProperty("Average")]
    public string Average { get; set; }

    [JsonProperty("TradingShares")]
    public string Volume { get; set; }

    [JsonProperty("TransactionAmount")]
    public string Turnover { get; set; }

    [JsonProperty("TransactionNumber")]
    public string TradesCount { get; set; }

    [JsonProperty("LatestBidPrice")]
    public string BestBidPrice { get; set; }

    [JsonProperty("LatesAskPrice")]
    public string BestAskPrice { get; set; }

    [JsonProperty("Capitals")]
    public string IssueSize { get; set; }

    [JsonProperty("NextReferencePrice")]
    public string NextReferencePrice { get; set; }

    [JsonProperty("NextLimitUp")]
    public string NextLimitUp { get; set; }

    [JsonProperty("NextLimitDown")]
    public string NextLimitDown { get; set; }
}

sealed class TpexEmergingRow
{
    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("Time")]
    public string Time { get; set; }

    [JsonProperty("SecuritiesCompanyCode")]
    public string Code { get; set; }

    [JsonProperty("CompanyName")]
    public string Name { get; set; }

    [JsonProperty("PreviousAveragePrice")]
    public string PreviousAveragePrice { get; set; }

    [JsonProperty("BuyingPrice")]
    public string BestBidPrice { get; set; }

    [JsonProperty("BuyingQuantity")]
    public string BestBidVolume { get; set; }

    [JsonProperty("SellingPrice")]
    public string BestAskPrice { get; set; }

    [JsonProperty("SellingQuantity")]
    public string BestAskVolume { get; set; }

    [JsonProperty("Highest")]
    public string High { get; set; }

    [JsonProperty("Lowest")]
    public string Low { get; set; }

    [JsonProperty("Average")]
    public string Average { get; set; }

    [JsonProperty("LatestPrice")]
    public string LastTradePrice { get; set; }

    [JsonProperty("TransactionVolume")]
    public string Volume { get; set; }
}

sealed class TpexValuationRow
{
    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("SecuritiesCompanyCode")]
    public string Code { get; set; }

    [JsonProperty("CompanyName")]
    public string Name { get; set; }

    [JsonProperty("PriceEarningRatio")]
    public string PriceEarnings { get; set; }

    [JsonProperty("DividendPerShare")]
    public string DividendPerShare { get; set; }

    [JsonProperty("YieldRatio")]
    public string DividendYield { get; set; }

    [JsonProperty("PriceBookRatio")]
    public string PriceBook { get; set; }
}

sealed class TpexSecurityProfile
{
    [JsonProperty("SecuritiesCompanyCode")]
    public string Code { get; set; }

    [JsonProperty("CompanyName")]
    public string Name { get; set; }

    [JsonProperty("CompanyAbbreviation")]
    public string ShortName { get; set; }

    [JsonProperty("SecuritiesIndustryCode")]
    public string IndustryCode { get; set; }

    [JsonProperty("DateOfListing")]
    public string ListingDate { get; set; }

    [JsonProperty("Symbol")]
    public string EnglishName { get; set; }

    [JsonProperty("IssueShares")]
    public string IssueSize { get; set; }

    [JsonIgnore]
    public bool IsEmerging { get; set; }

    [JsonIgnore]
    public SecurityTypes SecurityType { get; set; } =
        SecurityTypes.Stock;
}

sealed class TpexSnapshot
{
    public TpexMainboardRow[] MainboardPrices { get; set; } = [];
    public TpexEmergingRow[] EmergingPrices { get; set; } = [];
    public TpexValuationRow[] Valuations { get; set; } = [];
    public TpexSecurityProfile[] MainboardProfiles { get; set; } = [];
    public TpexSecurityProfile[] EmergingProfiles { get; set; } = [];
}

sealed class TpexHistoryRow
{
    public bool IsEmerging { get; set; }
    public string Date { get; set; }
    public string Open { get; set; }
    public string High { get; set; }
    public string Low { get; set; }
    public string Close { get; set; }
    public string Change { get; set; }
    public string Volume { get; set; }
    public string Turnover { get; set; }
    public string TradesCount { get; set; }
    public string SecondaryHigh { get; set; }
    public string SecondaryLow { get; set; }
    public string SecondaryAverage { get; set; }
    public string SecondaryVolume { get; set; }
    public string SecondaryTurnover { get; set; }
    public string SecondaryTradesCount { get; set; }
    public int VolumeMultiplier { get; set; } = 1;
    public int TurnoverMultiplier { get; set; } = 1;
}

sealed class TpexDailyRecord
{
    public DateTime TradingDate { get; set; }
    public DateTime ServerTime { get; set; }
    public SecurityId SecurityId { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? LastTradePrice { get; set; }
    public decimal? AveragePrice { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal? PriceChange { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Turnover { get; set; }
    public long? TradesCount { get; set; }
    public decimal? BestBidPrice { get; set; }
    public decimal? BestBidVolume { get; set; }
    public decimal? BestAskPrice { get; set; }
    public decimal? BestAskVolume { get; set; }
    public decimal? IssueSize { get; set; }
    public decimal? PriceEarnings { get; set; }
    public decimal? DividendYield { get; set; }
    public decimal? PriceBook { get; set; }
    public bool IsEmerging { get; set; }

    public DateTime OpenTime => TradingDate.ToTaipeiTime(
        TimeSpan.Zero);

    public bool HasOhlc =>
        OpenPrice is not null &&
        HighPrice is not null &&
        LowPrice is not null &&
        ClosePrice is not null;
}
