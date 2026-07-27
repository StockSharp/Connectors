namespace StockSharp.Ppi.Native;

sealed class PpiToken
{
    [JsonProperty("creationDate")]
    public DateTimeOffset CreationDate { get; set; }

    [JsonProperty("expirationDate")]
    public DateTimeOffset ExpirationDate { get; set; }

    [JsonProperty("accessToken")]
    public string AccessToken { get; set; }

    [JsonProperty("expires")]
    public int Expires { get; set; }

    [JsonProperty("refreshToken")]
    public string RefreshToken { get; set; }

    [JsonProperty("tokenType")]
    public string TokenType { get; set; }
}

sealed class PpiAccount
{
    [JsonProperty("accountNumber")]
    public string AccountNumber { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("externalID")]
    public string ExternalId { get; set; }
}

sealed class PpiAvailability
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("symbol")]
    public string Symbol { get; set; }

    [JsonProperty("simbol")]
    private string LegacySymbol
    {
        set
        {
            if (Symbol.IsEmpty())
                Symbol = value;
        }
    }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("settlement")]
    public string Settlement { get; set; }
}

sealed class PpiInstrument
{
    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("type")]
    public string Type { get; set; }

    [JsonProperty("market")]
    public string Market { get; set; }
}

sealed class PpiPrice
{
    [JsonProperty("date")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("volume")]
    public decimal Volume { get; set; }

    [JsonProperty("openingPrice")]
    public decimal OpeningPrice { get; set; }

    [JsonProperty("max")]
    public decimal High { get; set; }

    [JsonProperty("min")]
    public decimal Low { get; set; }

    [JsonProperty("previousClose")]
    public decimal PreviousClose { get; set; }

    [JsonProperty("marketChange")]
    public decimal MarketChange { get; set; }

    [JsonProperty("marketChangePercent")]
    public string MarketChangePercent { get; set; }
}

sealed class PpiBookLevel
{
    [JsonProperty("position")]
    public int Position { get; set; }

    [JsonProperty("price")]
    public decimal Price { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }
}

sealed class PpiBook
{
    [JsonProperty("date")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("offers")]
    public PpiBookLevel[] Offers { get; set; }

    [JsonProperty("bids")]
    public PpiBookLevel[] Bids { get; set; }
}

sealed class PpiDisclaimer
{
    [JsonProperty("code")]
    public string Code { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("mandatory")]
    public bool Mandatory { get; set; }

    [JsonProperty("accepted")]
    public bool Accepted { get; set; }
}

sealed class PpiOrder
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("instrumentType")]
    public string InstrumentType { get; set; }

    [JsonProperty("operation")]
    public string Operation { get; set; }

    [JsonProperty("ticker")]
    public string Ticker { get; set; }

    [JsonProperty("status")]
    public string Status { get; set; }

    [JsonProperty("date")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("settlement")]
    public string Settlement { get; set; }

    [JsonProperty("quantity")]
    public decimal Quantity { get; set; }

    [JsonProperty("orderType")]
    public string OrderType { get; set; }

    [JsonProperty("operationType")]
    public string OperationType { get; set; }

    [JsonProperty("operationMaxDate")]
    public DateTimeOffset? OperationMaxDate { get; set; }

    [JsonProperty("price")]
    public decimal? Price { get; set; }

    [JsonProperty("currency")]
    public string Currency { get; set; }

    [JsonProperty("amount")]
    public decimal Amount { get; set; }

    [JsonProperty("externalID")]
    public string ExternalId { get; set; }

    [JsonProperty("disclaimers")]
    public PpiDisclaimer[] Disclaimers { get; set; }
}

sealed class PpiMarketUpdate
{
    [JsonProperty("Ticker")]
    public string Ticker { get; set; }

    [JsonProperty("Price")]
    public decimal Price { get; set; }

    [JsonProperty("VolumeAmount")]
    public decimal VolumeAmount { get; set; }

    [JsonProperty("VolumeCurrency")]
    public decimal VolumeCurrency { get; set; }

    [JsonProperty("Date")]
    public DateTimeOffset Date { get; set; }

    [JsonProperty("Type")]
    public string Type { get; set; }

    [JsonProperty("Settlement")]
    public string Settlement { get; set; }

    [JsonProperty("VarDay")]
    public decimal Variation { get; set; }

    [JsonProperty("Offers")]
    public PpiBookLevel[] Offers { get; set; }

    [JsonProperty("Bids")]
    public PpiBookLevel[] Bids { get; set; }

    [JsonProperty("Trade")]
    public bool IsTrade { get; set; }

    [JsonProperty("OpeningPrice")]
    public decimal OpeningPrice { get; set; }

    [JsonProperty("MaxDay")]
    public decimal HighPrice { get; set; }

    [JsonProperty("MinDay")]
    public decimal LowPrice { get; set; }

    [JsonProperty("VolumeTotalAmount")]
    public decimal TotalVolume { get; set; }
}

sealed class PpiAccountUpdate
{
    [JsonProperty("Type")]
    public string Type { get; set; }

    [JsonProperty("Ticker")]
    public string Ticker { get; set; }

    [JsonProperty("OrderId")]
    public string OrderId { get; set; }

    [JsonProperty("QuantityExecuted")]
    public decimal QuantityExecuted { get; set; }

    [JsonProperty("Status")]
    public string Status { get; set; }

    [JsonProperty("LastUpdateDate")]
    public DateTimeOffset LastUpdateDate { get; set; }

    [JsonProperty("Operation")]
    public string Operation { get; set; }

    [JsonProperty("Message")]
    public string Message { get; set; }

    [JsonProperty("Date")]
    public DateTimeOffset Date { get; set; }
}
