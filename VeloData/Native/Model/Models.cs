namespace StockSharp.VeloData.Native.Model;

sealed class VeloDataInstrument
{
    public VeloDataMarketTypes MarketType { get; init; }
    public string Exchange { get; set; }
    public string Coin { get; set; }
    public string Product { get; set; }
    public long BeginMilliseconds { get; set; }
    public bool? IsDepth { get; set; }

    public string Key => MarketType.ToWire() + ":" + Exchange + ":" + Product;
    public string Code => (Product + "@" + Exchange + "-" +
        MarketType.ToWire()).ToUpperInvariant();
    public DateTime Begin => BeginMilliseconds.FromVeloMilliseconds();
}

sealed class VeloDataRow
{
    public long Time { get; set; }
    public string Exchange { get; set; }
    public string Coin { get; set; }
    public string Product { get; set; }
    public decimal? OpenPrice { get; set; }
    public decimal? HighPrice { get; set; }
    public decimal? LowPrice { get; set; }
    public decimal? ClosePrice { get; set; }
    public decimal? CoinVolume { get; set; }
    public decimal? TotalTrades { get; set; }
    public decimal? CoinOpenInterestClose { get; set; }
    public decimal? DvolOpen { get; set; }
    public decimal? DvolHigh { get; set; }
    public decimal? DvolLow { get; set; }
    public decimal? DvolClose { get; set; }
    public decimal? IndexPrice { get; set; }
}

sealed class VeloDataRowsRequest
{
    public VeloDataMarketTypes MarketType { get; init; }
    public string Exchange { get; init; }
    public string Product { get; init; }
    public VeloDataColumns[] Columns { get; init; }
    public DateTime Begin { get; init; }
    public DateTime End { get; init; }
    public TimeSpan Resolution { get; init; }
}

sealed class VeloDataNewsResponse
{
    [JsonProperty("stories")]
    public VeloDataNewsStory[] Stories { get; set; }
}

sealed class VeloDataNewsStory
{
    [JsonProperty("id")]
    public long? Id { get; set; }

    [JsonProperty("time")]
    public long? Time { get; set; }

    [JsonProperty("effectiveTime")]
    public long? EffectiveTime { get; set; }

    [JsonProperty("headline")]
    public string Headline { get; set; }

    [JsonProperty("source")]
    public string Source { get; set; }

    [JsonProperty("priority")]
    public int? Priority { get; set; }

    [JsonProperty("coins")]
    public string[] Coins { get; set; }

    [JsonProperty("summary")]
    public string Summary { get; set; }

    [JsonProperty("link")]
    public string Link { get; set; }

    [JsonProperty("edit")]
    public bool? IsEdit { get; set; }

    [JsonProperty("deleted")]
    public bool? IsDeleted { get; set; }

    [JsonProperty("heartbeat")]
    public bool? IsHeartbeat { get; set; }
}

sealed class VeloDataErrorResponse
{
    [JsonProperty("error")]
    public string Error { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("detail")]
    public string Detail { get; set; }
}
