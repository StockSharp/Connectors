namespace StockSharp.Tradernet.Native.Model;

sealed class TradernetSidInfo
{
    [JsonProperty("SID")]
    public string Sid { get; set; }

    [JsonProperty("user_id")]
    public long UserId { get; set; }
}

sealed class TradernetSearchResponse
{
    public TradernetSearchSecurity[] Found { get; set; }
    public int Code { get; set; }
}

sealed class TradernetSearchSecurity
{
    [JsonProperty("instr_id")]
    public long InstrumentId { get; set; }

    [JsonProperty("nm")]
    public string Name { get; set; }

    [JsonProperty("n")]
    public string ShortName { get; set; }

    [JsonProperty("ln")]
    public string LatinName { get; set; }

    [JsonProperty("t")]
    public string Ticker { get; set; }

    public string Isin { get; set; }
    public int Type { get; set; }
    public int Kind { get; set; }

    [JsonProperty("code_nm")]
    public string ExchangeTicker { get; set; }

    [JsonProperty("mkt_id")]
    public long MarketId { get; set; }

    [JsonProperty("mkt")]
    public string Market { get; set; }
}

sealed class TradernetSecurityInfo
{
    public long Id { get; set; }

    [JsonProperty("short_name")]
    public string ShortName { get; set; }

    [JsonProperty("default_ticker")]
    public string ExchangeTicker { get; set; }

    [JsonProperty("nt_ticker")]
    public string Ticker { get; set; }

    [JsonProperty("code_nm")]
    public string Code { get; set; }

    public string Currency { get; set; }

    [JsonProperty("min_step")]
    public string MinStep { get; set; }

    public string Lot { get; set; }

    [JsonProperty("code")]
    public int CodeValue { get; set; }
}

sealed class TradernetSecuritiesResponse
{
    public TradernetSecurity[] Securities { get; set; }
    public long? Total { get; set; }
}

sealed class TradernetSecurity
{
    public string Ticker { get; set; }

    [JsonProperty("instr_type_c")]
    public int Type { get; set; }

    [JsonProperty("instr_type")]
    public string TypeName { get; set; }

    [JsonProperty("instr_kind_c")]
    public int Kind { get; set; }

    [JsonProperty("instr_kind")]
    public string KindName { get; set; }

    [JsonProperty("instr_id")]
    public long InstrumentId { get; set; }

    [JsonProperty("code_nm")]
    public string ExchangeTicker { get; set; }

    public string Name { get; set; }

    [JsonProperty("name_alt")]
    public string AlternativeName { get; set; }

    [JsonProperty("issue_nb")]
    public string IssueNumber { get; set; }

    [JsonProperty("face_curr_c")]
    public string Currency { get; set; }

    [JsonProperty("mkt_id")]
    public long MarketId { get; set; }

    [JsonProperty("mkt_name")]
    public string MarketName { get; set; }

    [JsonProperty("mkt_short_code")]
    public string MarketCode { get; set; }

    [JsonProperty("lot_size_q")]
    public string LotSize { get; set; }

    [JsonProperty("maturity_d")]
    public string MaturityDate { get; set; }

    [JsonProperty("fv")]
    public string FaceValue { get; set; }

    [JsonProperty("step_price")]
    public string PriceStep { get; set; }

    [JsonProperty("x_descr")]
    public string Description { get; set; }

    public int IsTrade { get; set; }
}

sealed class TradernetQuoteResponse
{
    [JsonProperty("q")]
    public TradernetQuote[] Quotes { get; set; }
}

sealed class TradernetQuote
{
    [JsonProperty("c")]
    public string Ticker { get; set; }

    [JsonProperty("ltr")]
    public string LastTradeMarket { get; set; }

    public string Name { get; set; }
    public string Name2 { get; set; }

    [JsonProperty("bbp")]
    public string BestBidPrice { get; set; }

    [JsonProperty("bbs")]
    public string BestBidSize { get; set; }

    [JsonProperty("bap")]
    public string BestAskPrice { get; set; }

    [JsonProperty("bas")]
    public string BestAskSize { get; set; }

    [JsonProperty("pp")]
    public string PreviousPrice { get; set; }

    [JsonProperty("op")]
    public string OpenPrice { get; set; }

    [JsonProperty("ltp")]
    public string LastPrice { get; set; }

    [JsonProperty("lts")]
    public string LastSize { get; set; }

    [JsonProperty("ltt")]
    public string LastTime { get; set; }

    [JsonProperty("chg")]
    public string Change { get; set; }

    [JsonProperty("pcp")]
    public string ChangePercent { get; set; }

    [JsonProperty("mintp")]
    public string LowPrice { get; set; }

    [JsonProperty("maxtp")]
    public string HighPrice { get; set; }

    [JsonProperty("vol")]
    public string Volume { get; set; }

    [JsonProperty("vlt")]
    public string Turnover { get; set; }

    [JsonProperty("yld")]
    public string Yield { get; set; }

    [JsonProperty("acd")]
    public string AccruedInterest { get; set; }

    [JsonProperty("fv")]
    public string FaceValue { get; set; }

    [JsonProperty("mtd")]
    public string MaturityDate { get; set; }

    [JsonProperty("cpn")]
    public string Coupon { get; set; }

    [JsonProperty("trades")]
    public string TradesCount { get; set; }

    [JsonProperty("min_step")]
    public string MinStep { get; set; }

    [JsonProperty("step_price")]
    public string PriceStep { get; set; }

    [JsonProperty("strike_price")]
    public string StrikePrice { get; set; }

    public int Type { get; set; }
    public int Kind { get; set; }
}

sealed class TradernetBookBlock
{
    [JsonProperty("i")]
    public string Ticker { get; set; }

    [JsonProperty("cnt")]
    public int Count { get; set; }

    [JsonProperty("ins")]
    public TradernetBookRow[] Inserted { get; set; }

    [JsonProperty("del")]
    public TradernetBookRow[] Deleted { get; set; }

    [JsonProperty("upd")]
    public TradernetBookRow[] Updated { get; set; }
}

sealed class TradernetBookRow
{
    [JsonProperty("k")]
    public int Position { get; set; }

    [JsonProperty("p")]
    public string Price { get; set; }

    [JsonProperty("q")]
    public string Quantity { get; set; }

    [JsonProperty("s")]
    public string Side { get; set; }
}

sealed class TradernetPortfolio
{
    [JsonProperty("ps")]
    public TradernetPortfolio Nested { get; set; }

    public string Key { get; set; }

    [JsonProperty("acc")]
    public TradernetCashAccount[] Accounts { get; set; }

    [JsonProperty("pos")]
    public TradernetPosition[] Positions { get; set; }
}

sealed class TradernetCashAccount
{
    [JsonProperty("curr")]
    public string Currency { get; set; }

    [JsonProperty("currval")]
    public string CurrencyValue { get; set; }

    [JsonProperty("s")]
    public string Available { get; set; }

    [JsonProperty("forecast_in")]
    public string ForecastIn { get; set; }

    [JsonProperty("forecast_out")]
    public string ForecastOut { get; set; }

    [JsonProperty("t2_in")]
    public string T2In { get; set; }

    [JsonProperty("t2_out")]
    public string T2Out { get; set; }
}

sealed class TradernetPosition
{
    [JsonProperty("i")]
    public string Ticker { get; set; }

    [JsonProperty("q")]
    public string Quantity { get; set; }

    [JsonProperty("curr")]
    public string Currency { get; set; }

    public string Name { get; set; }
    public string Name2 { get; set; }

    [JsonProperty("mkt_price")]
    public string MarketPrice { get; set; }

    [JsonProperty("market_value")]
    public string MarketValue { get; set; }

    [JsonProperty("bal_price_a")]
    public string BalancePrice { get; set; }

    [JsonProperty("price_a")]
    public string OpenPrice { get; set; }

    [JsonProperty("profit_close")]
    public string RealizedPnl { get; set; }

    [JsonProperty("profit_price")]
    public string UnrealizedPnl { get; set; }

    [JsonProperty("go")]
    public string InitialMargin { get; set; }

    [JsonProperty("issue_nb")]
    public string IssueNumber { get; set; }

    [JsonProperty("instr_id")]
    public long InstrumentId { get; set; }
}

sealed class TradernetOrder
{
    [JsonProperty("order_id")]
    public long? OrderId { get; set; }

    [JsonProperty("id")]
    public long? HistoricalOrderId { get; set; }

    public string Date { get; set; }

    [JsonProperty("stat_d")]
    public string StatusDate { get; set; }

    [JsonProperty("instr")]
    public string Ticker { get; set; }

    [JsonProperty("oper")]
    public int Operation { get; set; }

    [JsonProperty("type")]
    public int Type { get; set; }

    [JsonProperty("cur")]
    public string Currency { get; set; }

    [JsonProperty("p")]
    public string Price { get; set; }

    [JsonProperty("stop")]
    public string StopPrice { get; set; }

    [JsonProperty("q")]
    public string Quantity { get; set; }

    [JsonProperty("leaves_qty")]
    public string LeavesQuantity { get; set; }

    [JsonProperty("exp")]
    public int Expiration { get; set; }

    [JsonProperty("stat")]
    public int Status { get; set; }

    [JsonProperty("user_order_id")]
    public long? UserOrderId { get; set; }

    [JsonProperty("userOrderId")]
    private long? LegacyUserOrderId
    {
        set => UserOrderId ??= value;
    }

    [JsonProperty("owner_login")]
    public string OwnerLogin { get; set; }

    [JsonProperty("login")]
    public string Login { get; set; }

    [JsonProperty("text")]
    public string Text { get; set; }

    [JsonProperty("@text")]
    public string AlternativeText { get; set; }

    [JsonProperty("trade")]
    public TradernetOwnTrade[] Trades { get; set; }
}

sealed class TradernetOwnTrade
{
    [JsonProperty("id")]
    public long Id { get; set; }

    [JsonProperty("order_id")]
    public long? OrderId { get; set; }

    [JsonProperty("p")]
    public string Price { get; set; }

    [JsonProperty("q")]
    public string Quantity { get; set; }

    public string Date { get; set; }
    public string Profit { get; set; }

    [JsonProperty("instr_nm")]
    public string Ticker { get; set; }

    [JsonProperty("curr_c")]
    public string Currency { get; set; }

    [JsonProperty("type")]
    public int Type { get; set; }
}

sealed class TradernetOrderResult
{
    [JsonProperty("order_id")]
    public long OrderId { get; set; }
}

sealed class TradernetPlaceOrder
{
    [JsonProperty("instr_name")]
    public string Ticker { get; set; }

    [JsonProperty("action_id")]
    public int Action { get; set; }

    [JsonProperty("order_type_id")]
    public int OrderType { get; set; }

    [JsonProperty("qty")]
    public long Quantity { get; set; }

    [JsonProperty("limit_price")]
    public decimal? LimitPrice { get; set; }

    [JsonProperty("stop_price")]
    public decimal? StopPrice { get; set; }

    [JsonProperty("expiration_id")]
    public int Expiration { get; set; }

    [JsonProperty("user_order_id")]
    public long? UserOrderId { get; set; }
}
