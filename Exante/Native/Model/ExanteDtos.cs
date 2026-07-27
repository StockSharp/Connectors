namespace StockSharp.Exante.Native.Model;

sealed class ExanteExchange
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Country { get; set; }
}

sealed class ExanteGroup
{
    public string Group { get; set; }
    public string Name { get; set; }
    public string Types { get; set; }
    public string Exchange { get; set; }
}

sealed class ExanteSymbol
{
    public string SymbolId { get; set; }
    public string Ticker { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public string Group { get; set; }
    public string UnderlyingSymbolId { get; set; }
    public string Exchange { get; set; }
    public string Expiration { get; set; }
    public string Country { get; set; }
    public string SymbolType { get; set; }
    public ExanteOptionData OptionData { get; set; }
    public string MinPriceIncrement { get; set; }
    public string Currency { get; set; }
    public ExanteIdentifiers Identifiers { get; set; }
    public string Icon { get; set; }
}

sealed class ExanteOptionData
{
    public string OptionGroupId { get; set; }
    public string OptionRight { get; set; }
    public string StrikePrice { get; set; }
}

sealed class ExanteIdentifiers
{
    [JsonProperty("ISIN")]
    public string Isin { get; set; }

    [JsonProperty("FIGI")]
    public string Figi { get; set; }

    [JsonProperty("CUSIP")]
    public string Cusip { get; set; }

    [JsonProperty("RIC")]
    public string Ric { get; set; }

    [JsonProperty("SEDOL")]
    public string Sedol { get; set; }

    [JsonProperty("CFI")]
    public string Cfi { get; set; }

    public string AssetClass { get; set; }
}

sealed class ExanteOhlc
{
    public long Timestamp { get; set; }
    public string Open { get; set; }
    public string High { get; set; }
    public string Low { get; set; }
    public string Close { get; set; }
    public string Volume { get; set; }
}

sealed class ExanteQuoteSide
{
    public string Price { get; set; }
    public string Value { get; set; }
    public string Size { get; set; }
}

sealed class ExanteQuote
{
    public long Timestamp { get; set; }
    public string SymbolId { get; set; }
    public ExanteQuoteSide[] Bid { get; set; }
    public ExanteQuoteSide[] Ask { get; set; }
    public string Event { get; set; }
    public string Reason { get; set; }
}

sealed class ExanteTradeTick
{
    public long Timestamp { get; set; }
    public string SymbolId { get; set; }
    public string Price { get; set; }
    public string Size { get; set; }
    public string Event { get; set; }
    public string Reason { get; set; }
}

sealed class ExanteAccount
{
    public string Status { get; set; }
    public string AccountId { get; set; }
}

sealed class ExanteAccountSummary
{
    public string AccountId { get; set; }
    public string Currency { get; set; }
    public string SessionDate { get; set; }
    public long Timestamp { get; set; }
    public string NetAssetValue { get; set; }
    public string FreeMoney { get; set; }
    public string MoneyUsedForMargin { get; set; }
    public string MarginUtilization { get; set; }
    public ExanteCurrencyPosition[] Currencies { get; set; }
    public ExanteInstrumentPosition[] Positions { get; set; }
}

sealed class ExanteCurrencyPosition
{
    public string Code { get; set; }
    public string Value { get; set; }
    public string ConvertedValue { get; set; }
}

sealed class ExanteInstrumentPosition
{
    public string SymbolId { get; set; }
    public string SymbolType { get; set; }
    public string Quantity { get; set; }
    public string Currency { get; set; }
    public string Price { get; set; }
    public string AveragePrice { get; set; }
    public string Pnl { get; set; }
    public string ConvertedPnl { get; set; }
    public string Value { get; set; }
    public string ConvertedValue { get; set; }
    public string AccruedInterest { get; set; }
}

sealed class ExanteOrder
{
    public string Id { get; set; }
    public string OrderId { get; set; }
    public string PlaceTime { get; set; }
    public string CurrentModificationId { get; set; }
    public string AccountId { get; set; }
    public string Username { get; set; }
    public string ClientTag { get; set; }
    public ExanteOrderState OrderState { get; set; }
    public ExanteOrderParameters OrderParameters { get; set; }
}

sealed class ExanteOrderState
{
    public string Status { get; set; }
    public string LastUpdate { get; set; }
    public ExanteOrderFill[] Fills { get; set; }
    public string Reason { get; set; }
}

sealed class ExanteOrderFill
{
    public string Time { get; set; }
    public string Timestamp { get; set; }
    public string Quantity { get; set; }
    public string Price { get; set; }
    public int Position { get; set; }
}

sealed class ExanteOrderParameters
{
    public string Side { get; set; }
    public string Quantity { get; set; }
    public string OcoGroup { get; set; }
    public string IfDoneParentId { get; set; }
    public string Duration { get; set; }
    public string OrderType { get; set; }
    public string StopPrice { get; set; }
    public string LimitPrice { get; set; }
    public string PartQuantity { get; set; }
    public long? PlaceInterval { get; set; }
    public string PriceDistance { get; set; }
    public string GttExpiration { get; set; }
    public string Instrument { get; set; }
    public string SymbolId { get; set; }
}

sealed class ExantePlaceOrder
{
    public string AccountId { get; set; }
    public string SymbolId { get; set; }
    public string Side { get; set; }
    public string Quantity { get; set; }
    public string OrderType { get; set; }
    public string StopPrice { get; set; }
    public string LimitPrice { get; set; }
    public string PartQuantity { get; set; }
    public string PlaceInterval { get; set; }
    public string PriceDistance { get; set; }
    public string Duration { get; set; }
    public string GttExpiration { get; set; }
    public string ClientTag { get; set; }
    public string TakeProfit { get; set; }
    public string StopLoss { get; set; }
    public string OcoGroup { get; set; }
    public string IfDoneParentId { get; set; }
}

sealed class ExanteModifyOrder
{
    public string Action { get; set; }
    public ExanteReplaceOrder Parameters { get; set; }
}

sealed class ExanteReplaceOrder
{
    public string Quantity { get; set; }
    public string LimitPrice { get; set; }
    public string StopPrice { get; set; }
    public string PriceDistance { get; set; }
}

sealed class ExanteOrderUpdate
{
    public string Event { get; set; }
    public ExanteOrder Order { get; set; }
}

sealed class ExantePrivateTrade
{
    public string Event { get; set; }
    public string OrderId { get; set; }
    public string Time { get; set; }
    public string Timestamp { get; set; }
    public string Quantity { get; set; }
    public string Price { get; set; }
    public int Position { get; set; }
}

sealed class ExanteApiError
{
    public string Group { get; set; }
    public string Message { get; set; }
}
