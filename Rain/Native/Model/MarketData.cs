namespace StockSharp.Rain.Native.Model;

sealed class RainCurrency
{
    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("code")]
    public string Code { get; init; }

    [JsonProperty("precision")]
    public int Precision { get; init; }

    [JsonProperty("digital")]
    public bool IsDigital { get; init; }
}

sealed class RainProduct
{
    [JsonProperty("symbol")]
    public string Symbol { get; init; }

    [JsonProperty("ref_precision")]
    public decimal ReferencePrecision { get; init; }

    [JsonProperty("base_precision")]
    public decimal BasePrecision { get; init; }

    [JsonProperty("minimum")]
    public decimal Minimum { get; init; }

    [JsonProperty("base_currency")]
    public RainCurrency BaseCurrency { get; init; }

    [JsonProperty("ref_currency")]
    public RainCurrency ReferenceCurrency { get; init; }

    [JsonProperty("bid_price")]
    public RainAmount BidPrice { get; init; }

    [JsonProperty("ask_price")]
    public RainAmount AskPrice { get; init; }

    [JsonProperty("last_price")]
    public RainAmount LastPrice { get; init; }

    [JsonProperty("volume")]
    public RainAmount Volume { get; init; }

    [JsonProperty("change")]
    public decimal? Change { get; init; }
}

sealed class RainCandle
{
    [JsonProperty("open")]
    public decimal Open { get; init; }

    [JsonProperty("high")]
    public decimal High { get; init; }

    [JsonProperty("low")]
    public decimal Low { get; init; }

    [JsonProperty("close")]
    public decimal Close { get; init; }

    [JsonProperty("volume")]
    public decimal Volume { get; init; }

    [JsonProperty("time")]
    public DateTime Time { get; init; }
}

sealed class RainBookLevel
{
    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum RainSides
{
    [EnumMember(Value = "buy")]
    Buy,

    [EnumMember(Value = "sell")]
    Sell,
}

sealed class RainPublicTrade
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; init; }

    [JsonProperty("price")]
    public decimal Price { get; init; }

    [JsonProperty("date")]
    public DateTime Date { get; init; }

    [JsonProperty("side")]
    public RainSides Side { get; init; }
}
