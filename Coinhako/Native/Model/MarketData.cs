namespace StockSharp.Coinhako.Native.Model;

sealed class CoinhakoSpotPrice
{
    [JsonProperty("buy_price")]
    public decimal BuyPrice { get; set; }

    [JsonProperty("sell_price")]
    public decimal SellPrice { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }
}

sealed class CoinhakoSpotsQuery : CoinhakoQueryParameters
{
    public string BaseCurrency { get; init; }
    public string CounterCurrency { get; init; }

    public override void Append(CoinhakoQueryWriter writer)
    {
        writer.Add("base_currency", BaseCurrency);
        writer.Add("counter_currency", CounterCurrency);
    }
}
