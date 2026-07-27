namespace StockSharp.Twse.Native;

sealed class TwseDailyRow
{
    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("Code")]
    public string Code { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("TradeVolume")]
    public string TradeVolume { get; set; }

    [JsonProperty("TradeValue")]
    public string TradeValue { get; set; }

    [JsonProperty("OpeningPrice")]
    public string OpeningPrice { get; set; }

    [JsonProperty("HighestPrice")]
    public string HighestPrice { get; set; }

    [JsonProperty("LowestPrice")]
    public string LowestPrice { get; set; }

    [JsonProperty("ClosingPrice")]
    public string ClosingPrice { get; set; }

    [JsonProperty("Change")]
    public string Change { get; set; }

    [JsonProperty("Transaction")]
    public string Transaction { get; set; }
}

sealed class TwseValuationRow
{
    [JsonProperty("Date")]
    public string Date { get; set; }

    [JsonProperty("Code")]
    public string Code { get; set; }

    [JsonProperty("Name")]
    public string Name { get; set; }

    [JsonProperty("PEratio")]
    public string PriceEarnings { get; set; }

    [JsonProperty("DividendYield")]
    public string DividendYield { get; set; }

    [JsonProperty("PBratio")]
    public string PriceBook { get; set; }
}

sealed class TwseSecurityProfile
{
    public string Code { get; set; }
    public string Name { get; set; }
    public string ShortName { get; set; }
    public string EnglishName { get; set; }
    public string Class { get; set; }
    public string ListingDate { get; set; }
    public string IssueSize { get; set; }
    public SecurityTypes SecurityType { get; set; }
}

sealed class TwseSnapshot
{
    public TwseDailyRow[] Prices { get; set; } = [];
    public TwseValuationRow[] Valuations { get; set; } = [];
    public TwseSecurityProfile[] Profiles { get; set; } = [];
}

sealed class TwseDailyRecord
{
    public DateTime TradingDate { get; set; }
    public SecurityId SecurityId { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? PreviousClosePrice { get; set; }
    public decimal? PriceChange { get; set; }
    public decimal? Volume { get; set; }
    public decimal? Turnover { get; set; }
    public long? TradesCount { get; set; }
    public decimal? PriceEarnings { get; set; }
    public decimal? DividendYield { get; set; }
    public decimal? PriceBook { get; set; }

    public DateTime OpenTime => TradingDate.ToTaipeiTime(
        TimeSpan.Zero);

    public DateTime CloseTime => TradingDate.ToTaipeiTime(
        new TimeSpan(13, 30, 0));

    public bool HasOhlc =>
        OpenPrice is not null &&
        HighPrice is not null &&
        LowPrice is not null &&
        ClosePrice is not null;
}
