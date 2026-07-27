namespace StockSharp.SetMarketData.Native;

sealed class SetBookLevel
{
    [JsonProperty("rank")]
    public int Rank { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }
}

sealed class SetStockQuote
{
    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("fullName")]
    public string FullName { get; set; }

    [JsonProperty("market")]
    public string Market { get; set; }

    [JsonProperty("securityType")]
    public string SecurityType { get; set; }

    [JsonProperty("industry")]
    public string Industry { get; set; }

    [JsonProperty("sector")]
    public string Sector { get; set; }

    [JsonProperty("prior")]
    public decimal? Prior { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("project1")]
    public decimal? ProjectedOpen1 { get; set; }

    [JsonProperty("project2")]
    public decimal? ProjectedOpen2 { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("last")]
    public decimal? Last { get; set; }

    [JsonProperty("average")]
    public decimal? Average { get; set; }

    [JsonProperty("aomVolume")]
    public decimal? AomVolume { get; set; }

    [JsonProperty("aomValue")]
    public decimal? AomValue { get; set; }

    [JsonProperty("trVolume")]
    public decimal? TradeReportVolume { get; set; }

    [JsonProperty("trValue")]
    public decimal? TradeReportValue { get; set; }

    [JsonProperty("totalVolume")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("totalValue")]
    public decimal? TotalValue { get; set; }

    [JsonProperty("inav")]
    public decimal? IndicativeNav { get; set; }

    [JsonProperty("changeInav")]
    public decimal? IndicativeNavChange { get; set; }

    [JsonProperty("percentChangeInav")]
    public decimal? IndicativeNavPercentChange { get; set; }

    [JsonProperty("timeInav")]
    public string IndicativeNavTime { get; set; }

    [JsonProperty("bid")]
    public SetBookLevel[] Bids { get; set; }

    [JsonProperty("offer")]
    public SetBookLevel[] Offers { get; set; }
}

sealed class SetIndexQuote
{
    [JsonProperty("time")]
    public string Time { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("fullName")]
    public string FullName { get; set; }

    [JsonProperty("prior")]
    public decimal? Prior { get; set; }

    [JsonProperty("open")]
    public decimal? Open { get; set; }

    [JsonProperty("high")]
    public decimal? High { get; set; }

    [JsonProperty("low")]
    public decimal? Low { get; set; }

    [JsonProperty("last")]
    public decimal? Last { get; set; }

    [JsonProperty("volume")]
    public decimal? Volume { get; set; }

    [JsonProperty("value")]
    public decimal? Value { get; set; }

    [JsonProperty("totalVolume")]
    public decimal? TotalVolume { get; set; }

    [JsonProperty("totalValue")]
    public decimal? TotalValue { get; set; }
}

readonly record struct SetStockQuery(
    string Markets,
    string IndexSectors,
    string SecurityTypes,
    string Symbols,
    bool OddLots);

readonly record struct SetIndexQuery(
    string Markets,
    string IndexSectors);
