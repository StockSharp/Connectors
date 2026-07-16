namespace StockSharp.LsSecurities.Native;

internal abstract class LsResponse
{
	[JsonProperty("rsp_cd")]
	public string ResponseCode { get; set; }

	[JsonProperty("rsp_msg")]
	public string ResponseMessage { get; set; }
}

internal sealed class LsTokenRequest
{
	public string GrantType { get; set; } = "client_credentials";
	public string AppKey { get; set; }
	public string AppSecret { get; set; }
	public string Scope { get; set; } = "oob";
}

internal sealed class LsTokenResponse
{
	[JsonProperty("access_token")]
	public string AccessToken { get; set; }

	[JsonProperty("scope")]
	public string Scope { get; set; }

	[JsonProperty("token_type")]
	public string TokenType { get; set; }

	[JsonProperty("expires_in")]
	public long ExpiresIn { get; set; }
}

internal sealed class LsInstrumentRequest
{
	[JsonProperty("t8436InBlock")]
	public LsInstrumentRequestBlock Data { get; set; } = new();
}

internal sealed class LsInstrumentRequestBlock
{
	[JsonProperty("gubun")]
	public string Market { get; set; } = "0";
}

internal sealed class LsInstrumentResponse : LsResponse
{
	[JsonProperty("t8436OutBlock")]
	public LsInstrument[] Instruments { get; set; }
}

internal sealed class LsInstrument
{
	[JsonProperty("shcode")]
	public string Code { get; set; }

	[JsonProperty("expcode")]
	public string Isin { get; set; }

	[JsonProperty("hname")]
	public string Name { get; set; }

	[JsonProperty("gubun")]
	public string Market { get; set; }

	[JsonProperty("etfgubun")]
	public string EtfType { get; set; }

	[JsonProperty("spac_gubun")]
	public string IsSpac { get; set; }

	[JsonProperty("jnilclose")]
	public decimal PreviousClose { get; set; }

	[JsonProperty("uplmtprice")]
	public decimal UpperLimit { get; set; }

	[JsonProperty("dnlmtprice")]
	public decimal LowerLimit { get; set; }

	[JsonProperty("recprice")]
	public decimal ReferencePrice { get; set; }

	[JsonProperty("memedan")]
	public string LotSize { get; set; }
}

internal sealed class LsQuoteRequest
{
	[JsonProperty("t1101InBlock")]
	public LsSymbolRequest Data { get; set; } = new();
}

internal sealed class LsSymbolRequest
{
	[JsonProperty("shcode")]
	public string Code { get; set; }
}

internal sealed class LsQuoteResponse : LsResponse
{
	[JsonProperty("t1101OutBlock")]
	public LsQuote Quote { get; set; }
}

internal sealed class LsQuote
{
	[JsonProperty("shcode")]
	public string Code { get; set; }

	[JsonProperty("hname")]
	public string Name { get; set; }

	[JsonProperty("hotime")]
	public string Time { get; set; }

	[JsonProperty("price")]
	public decimal LastPrice { get; set; }

	[JsonProperty("open")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("high")]
	public decimal HighPrice { get; set; }

	[JsonProperty("low")]
	public decimal LowPrice { get; set; }

	[JsonProperty("jnilclose")]
	public decimal PreviousClose { get; set; }

	[JsonProperty("volume")]
	public decimal Volume { get; set; }

	[JsonProperty("offer")]
	public decimal TotalAskVolume { get; set; }

	[JsonProperty("bid")]
	public decimal TotalBidVolume { get; set; }

	[JsonProperty("offerho1")] public decimal AskPrice1 { get; set; }
	[JsonProperty("offerho2")] public decimal AskPrice2 { get; set; }
	[JsonProperty("offerho3")] public decimal AskPrice3 { get; set; }
	[JsonProperty("offerho4")] public decimal AskPrice4 { get; set; }
	[JsonProperty("offerho5")] public decimal AskPrice5 { get; set; }
	[JsonProperty("offerho6")] public decimal AskPrice6 { get; set; }
	[JsonProperty("offerho7")] public decimal AskPrice7 { get; set; }
	[JsonProperty("offerho8")] public decimal AskPrice8 { get; set; }
	[JsonProperty("offerho9")] public decimal AskPrice9 { get; set; }
	[JsonProperty("offerho10")] public decimal AskPrice10 { get; set; }
	[JsonProperty("offerrem1")] public decimal AskVolume1 { get; set; }
	[JsonProperty("offerrem2")] public decimal AskVolume2 { get; set; }
	[JsonProperty("offerrem3")] public decimal AskVolume3 { get; set; }
	[JsonProperty("offerrem4")] public decimal AskVolume4 { get; set; }
	[JsonProperty("offerrem5")] public decimal AskVolume5 { get; set; }
	[JsonProperty("offerrem6")] public decimal AskVolume6 { get; set; }
	[JsonProperty("offerrem7")] public decimal AskVolume7 { get; set; }
	[JsonProperty("offerrem8")] public decimal AskVolume8 { get; set; }
	[JsonProperty("offerrem9")] public decimal AskVolume9 { get; set; }
	[JsonProperty("offerrem10")] public decimal AskVolume10 { get; set; }
	[JsonProperty("bidho1")] public decimal BidPrice1 { get; set; }
	[JsonProperty("bidho2")] public decimal BidPrice2 { get; set; }
	[JsonProperty("bidho3")] public decimal BidPrice3 { get; set; }
	[JsonProperty("bidho4")] public decimal BidPrice4 { get; set; }
	[JsonProperty("bidho5")] public decimal BidPrice5 { get; set; }
	[JsonProperty("bidho6")] public decimal BidPrice6 { get; set; }
	[JsonProperty("bidho7")] public decimal BidPrice7 { get; set; }
	[JsonProperty("bidho8")] public decimal BidPrice8 { get; set; }
	[JsonProperty("bidho9")] public decimal BidPrice9 { get; set; }
	[JsonProperty("bidho10")] public decimal BidPrice10 { get; set; }
	[JsonProperty("bidrem1")] public decimal BidVolume1 { get; set; }
	[JsonProperty("bidrem2")] public decimal BidVolume2 { get; set; }
	[JsonProperty("bidrem3")] public decimal BidVolume3 { get; set; }
	[JsonProperty("bidrem4")] public decimal BidVolume4 { get; set; }
	[JsonProperty("bidrem5")] public decimal BidVolume5 { get; set; }
	[JsonProperty("bidrem6")] public decimal BidVolume6 { get; set; }
	[JsonProperty("bidrem7")] public decimal BidVolume7 { get; set; }
	[JsonProperty("bidrem8")] public decimal BidVolume8 { get; set; }
	[JsonProperty("bidrem9")] public decimal BidVolume9 { get; set; }
	[JsonProperty("bidrem10")] public decimal BidVolume10 { get; set; }

	public QuoteChange[] GetBids()
		=> CreateDepth(
			[BidPrice1, BidPrice2, BidPrice3, BidPrice4, BidPrice5, BidPrice6, BidPrice7, BidPrice8, BidPrice9, BidPrice10],
			[BidVolume1, BidVolume2, BidVolume3, BidVolume4, BidVolume5, BidVolume6, BidVolume7, BidVolume8, BidVolume9, BidVolume10]);

	public QuoteChange[] GetAsks()
		=> CreateDepth(
			[AskPrice1, AskPrice2, AskPrice3, AskPrice4, AskPrice5, AskPrice6, AskPrice7, AskPrice8, AskPrice9, AskPrice10],
			[AskVolume1, AskVolume2, AskVolume3, AskVolume4, AskVolume5, AskVolume6, AskVolume7, AskVolume8, AskVolume9, AskVolume10]);

	private static QuoteChange[] CreateDepth(decimal[] prices, decimal[] volumes)
		=> [.. prices.Select((price, index) => new QuoteChange(price, volumes[index]))
			.Where(quote => quote.Price > 0)];
}

internal sealed class LsTickHistoryRequest
{
	[JsonProperty("t1301InBlock")]
	public LsTickHistoryRequestBlock Data { get; set; } = new();
}

internal sealed class LsTickHistoryRequestBlock
{
	[JsonProperty("shcode")] public string Code { get; set; }
	[JsonProperty("cvolume")] public decimal MinimumVolume { get; set; }
	[JsonProperty("starttime")] public string StartTime { get; set; }
	[JsonProperty("endtime")] public string EndTime { get; set; }
	[JsonProperty("cts_time")] public string ContinuationTime { get; set; }
}

internal sealed class LsTickHistoryResponse : LsResponse
{
	[JsonProperty("t1301OutBlock")]
	public LsTickHistoryContinuation Continuation { get; set; }

	[JsonProperty("t1301OutBlock1")]
	public LsHistoricalTick[] Ticks { get; set; }
}

internal sealed class LsTickHistoryContinuation
{
	[JsonProperty("cts_time")]
	public string Time { get; set; }
}

internal sealed class LsHistoricalTick
{
	[JsonProperty("chetime")] public string Time { get; set; }
	[JsonProperty("price")] public decimal Price { get; set; }
	[JsonProperty("cvolume")] public decimal Volume { get; set; }
	[JsonProperty("volume")] public decimal TotalVolume { get; set; }
	[JsonProperty("mdvolume")] public decimal SellVolume { get; set; }
	[JsonProperty("msvolume")] public decimal BuyVolume { get; set; }
	[JsonProperty("sign")] public string Sign { get; set; }
}

internal sealed class LsMinuteChartRequest
{
	[JsonProperty("t8412InBlock")]
	public LsMinuteChartRequestBlock Data { get; set; } = new();
}

internal sealed class LsMinuteChartRequestBlock
{
	[JsonProperty("shcode")] public string Code { get; set; }
	[JsonProperty("ncnt")] public int Minutes { get; set; }
	[JsonProperty("qrycnt")] public int Count { get; set; } = 500;
	[JsonProperty("nday")] public string Days { get; set; } = "0";
	[JsonProperty("sdate")] public string StartDate { get; set; }
	[JsonProperty("stime")] public string StartTime { get; set; }
	[JsonProperty("edate")] public string EndDate { get; set; } = "99999999";
	[JsonProperty("etime")] public string EndTime { get; set; }
	[JsonProperty("cts_date")] public string ContinuationDate { get; set; }
	[JsonProperty("cts_time")] public string ContinuationTime { get; set; }
	[JsonProperty("comp_yn")] public string IsCompressed { get; set; } = "N";
}

internal sealed class LsMinuteChartResponse : LsResponse
{
	[JsonProperty("t8412OutBlock")]
	public LsChartContinuation Continuation { get; set; }

	[JsonProperty("t8412OutBlock1")]
	public LsCandle[] Candles { get; set; }
}

internal sealed class LsDayChartRequest
{
	[JsonProperty("t8410InBlock")]
	public LsDayChartRequestBlock Data { get; set; } = new();
}

internal sealed class LsDayChartRequestBlock
{
	[JsonProperty("shcode")] public string Code { get; set; }
	[JsonProperty("gubun")] public string Period { get; set; } = "2";
	[JsonProperty("qrycnt")] public int Count { get; set; } = 500;
	[JsonProperty("sdate")] public string StartDate { get; set; }
	[JsonProperty("edate")] public string EndDate { get; set; }
	[JsonProperty("cts_date")] public string ContinuationDate { get; set; }
	[JsonProperty("comp_yn")] public string IsCompressed { get; set; } = "N";
	[JsonProperty("sujung")] public string IsAdjusted { get; set; } = "Y";
}

internal sealed class LsDayChartResponse : LsResponse
{
	[JsonProperty("t8410OutBlock")]
	public LsChartContinuation Continuation { get; set; }

	[JsonProperty("t8410OutBlock1")]
	public LsCandle[] Candles { get; set; }
}

internal sealed class LsChartContinuation
{
	[JsonProperty("cts_date")] public string Date { get; set; }
	[JsonProperty("cts_time")] public string Time { get; set; }
}

internal sealed class LsCandle
{
	[JsonProperty("date")] public string Date { get; set; }
	[JsonProperty("time")] public string Time { get; set; }
	[JsonProperty("open")] public decimal OpenPrice { get; set; }
	[JsonProperty("high")] public decimal HighPrice { get; set; }
	[JsonProperty("low")] public decimal LowPrice { get; set; }
	[JsonProperty("close")] public decimal ClosePrice { get; set; }
	[JsonProperty("jdiff_vol")] public decimal Volume { get; set; }
	[JsonProperty("value")] public decimal Turnover { get; set; }
}

internal sealed class LsPlaceOrderRequest
{
	[JsonProperty("CSPAT00601InBlock1")]
	public LsPlaceOrderRequestBlock Data { get; set; } = new();
}

internal sealed class LsPlaceOrderRequestBlock
{
	[JsonProperty("IsuNo")] public string SecurityCode { get; set; }
	[JsonProperty("OrdQty")] public decimal Quantity { get; set; }
	[JsonProperty("OrdPrc")] public decimal Price { get; set; }
	[JsonProperty("BnsTpCode")] public string Side { get; set; }
	[JsonProperty("OrdprcPtnCode")] public string PriceType { get; set; }
	[JsonProperty("MgntrnCode")] public string MarginTransactionCode { get; set; }
	[JsonProperty("LoanDt")] public string LoanDate { get; set; }
	[JsonProperty("OrdCndiTpCode")] public string ConditionType { get; set; }
	[JsonProperty("MbrNo")] public string MemberNumber { get; set; }
}

internal sealed class LsPlaceOrderResponse : LsResponse
{
	[JsonProperty("CSPAT00601OutBlock2")]
	public LsOrderResult Result { get; set; }
}

internal sealed class LsReplaceOrderRequest
{
	[JsonProperty("CSPAT00701InBlock1")]
	public LsReplaceOrderRequestBlock Data { get; set; } = new();
}

internal sealed class LsReplaceOrderRequestBlock
{
	[JsonProperty("OrgOrdNo")] public long OriginalOrderNumber { get; set; }
	[JsonProperty("IsuNo")] public string SecurityCode { get; set; }
	[JsonProperty("OrdQty")] public decimal Quantity { get; set; }
	[JsonProperty("OrdprcPtnCode")] public string PriceType { get; set; }
	[JsonProperty("OrdCndiTpCode")] public string ConditionType { get; set; }
	[JsonProperty("OrdPrc")] public decimal Price { get; set; }
}

internal sealed class LsReplaceOrderResponse : LsResponse
{
	[JsonProperty("CSPAT00701OutBlock2")]
	public LsOrderResult Result { get; set; }
}

internal sealed class LsCancelOrderRequest
{
	[JsonProperty("CSPAT00801InBlock1")]
	public LsCancelOrderRequestBlock Data { get; set; } = new();
}

internal sealed class LsCancelOrderRequestBlock
{
	[JsonProperty("OrgOrdNo")] public long OriginalOrderNumber { get; set; }
	[JsonProperty("IsuNo")] public string SecurityCode { get; set; }
	[JsonProperty("OrdQty")] public decimal Quantity { get; set; }
}

internal sealed class LsCancelOrderResponse : LsResponse
{
	[JsonProperty("CSPAT00801OutBlock2")]
	public LsOrderResult Result { get; set; }
}

internal sealed class LsOrderResult
{
	[JsonProperty("OrdNo")] public long OrderNumber { get; set; }
	[JsonProperty("PrntOrdNo")] public long ParentOrderNumber { get; set; }
	[JsonProperty("OrdTime")] public string Time { get; set; }
	[JsonProperty("ShtnIsuNo")] public string SecurityCode { get; set; }
	[JsonProperty("OrdAmt")] public decimal Amount { get; set; }
	[JsonProperty("IsuNm")] public string SecurityName { get; set; }
}

internal sealed class LsPositionsRequest
{
	[JsonProperty("t0424InBlock")]
	public LsPositionsRequestBlock Data { get; set; } = new();
}

internal sealed class LsPositionsRequestBlock
{
	[JsonProperty("prcgb")] public string PriceType { get; set; } = "1";
	[JsonProperty("chegb")] public string BalanceType { get; set; } = "2";
	[JsonProperty("dangb")] public string SessionType { get; set; } = "0";
	[JsonProperty("charge")] public string IncludeCharges { get; set; } = "1";
	[JsonProperty("cts_expcode")] public string ContinuationCode { get; set; }
}

internal sealed class LsPositionsResponse : LsResponse
{
	[JsonProperty("t0424OutBlock")]
	public LsPortfolioSummary Summary { get; set; }

	[JsonProperty("t0424OutBlock1")]
	public LsPosition[] Positions { get; set; }
}

internal sealed class LsPortfolioSummary
{
	[JsonProperty("cts_expcode")] public string ContinuationCode { get; set; }
	[JsonProperty("mamt")] public decimal PurchaseAmount { get; set; }
	[JsonProperty("tappamt")] public decimal EvaluationAmount { get; set; }
	[JsonProperty("sunamt")] public decimal EstimatedAssets { get; set; }
	[JsonProperty("tdtsunik")] public decimal ProfitLoss { get; set; }
}

internal sealed class LsPosition
{
	[JsonProperty("expcode")] public string Code { get; set; }
	[JsonProperty("hname")] public string Name { get; set; }
	[JsonProperty("janqty")] public decimal Quantity { get; set; }
	[JsonProperty("mdposqt")] public decimal SellableQuantity { get; set; }
	[JsonProperty("pamt")] public decimal AveragePrice { get; set; }
	[JsonProperty("price")] public decimal CurrentPrice { get; set; }
	[JsonProperty("appamt")] public decimal MarketValue { get; set; }
	[JsonProperty("dtsunik")] public decimal ProfitLoss { get; set; }
}

internal sealed class LsOrdersRequest
{
	[JsonProperty("t0425InBlock")]
	public LsOrdersRequestBlock Data { get; set; } = new();
}

internal sealed class LsOrdersRequestBlock
{
	[JsonProperty("expcode")] public string Code { get; set; }
	[JsonProperty("chegb")] public string ExecutionType { get; set; } = "0";
	[JsonProperty("medosu")] public string Side { get; set; } = "0";
	[JsonProperty("sortgb")] public string Sort { get; set; } = "2";
	[JsonProperty("cts_ordno")] public string ContinuationOrderNumber { get; set; }
}

internal sealed class LsOrdersResponse : LsResponse
{
	[JsonProperty("t0425OutBlock")]
	public LsOrdersSummary Summary { get; set; }

	[JsonProperty("t0425OutBlock1")]
	public LsOrder[] Orders { get; set; }
}

internal sealed class LsOrdersSummary
{
	[JsonProperty("cts_ordno")] public string ContinuationOrderNumber { get; set; }
}

internal sealed class LsOrder
{
	[JsonProperty("ordno")] public long OrderNumber { get; set; }
	[JsonProperty("orgordno")] public long OriginalOrderNumber { get; set; }
	[JsonProperty("expcode")] public string Code { get; set; }
	[JsonProperty("medosu")] public string Side { get; set; }
	[JsonProperty("qty")] public decimal Quantity { get; set; }
	[JsonProperty("price")] public decimal OrderPrice { get; set; }
	[JsonProperty("ordrem")] public decimal Balance { get; set; }
	[JsonProperty("cheqty")] public decimal FilledQuantity { get; set; }
	[JsonProperty("cheprice")] public decimal ExecutionPrice { get; set; }
	[JsonProperty("ordtime")] public string Time { get; set; }
	[JsonProperty("hogagb")] public string PriceType { get; set; }
	[JsonProperty("status")] public string Status { get; set; }
	[JsonProperty("orggb")] public string OriginalType { get; set; }
}

internal sealed class LsSocketRequest
{
	[JsonProperty("header")]
	public LsSocketRequestHeader Header { get; set; } = new();

	[JsonProperty("body")]
	public LsSocketRequestBody Body { get; set; } = new();
}

internal sealed class LsSocketRequestHeader
{
	[JsonProperty("token")] public string Token { get; set; }
	[JsonProperty("tr_type")] public string Type { get; set; }
}

internal sealed class LsSocketRequestBody
{
	[JsonProperty("tr_cd")] public string Code { get; set; }
	[JsonProperty("tr_key")] public string Key { get; set; }
}

internal sealed class LsSocketDiscriminator
{
	[JsonProperty("header")]
	public LsSocketResponseHeader Header { get; set; }
}

internal sealed class LsSocketResponseHeader
{
	[JsonProperty("tr_cd")] public string Code { get; set; }
	[JsonProperty("tr_key")] public string Key { get; set; }
	[JsonProperty("rsp_cd")] public string ResponseCode { get; set; }
	[JsonProperty("rsp_msg")] public string ResponseMessage { get; set; }
}

internal sealed class LsSocketEnvelope<TBody>
{
	[JsonProperty("header")]
	public LsSocketResponseHeader Header { get; set; }

	[JsonProperty("body")]
	public TBody Body { get; set; }
}

internal sealed class LsRealtimeTrade
{
	[JsonProperty("shcode")] public string Code { get; set; }
	[JsonProperty("ex_shcode")] public string ExchangeCode { get; set; }
	[JsonProperty("chetime")] public string Time { get; set; }
	[JsonProperty("price")] public string Price { get; set; }
	[JsonProperty("cvolume")] public string Volume { get; set; }
	[JsonProperty("volume")] public string TotalVolume { get; set; }
	[JsonProperty("value")] public string Turnover { get; set; }
	[JsonProperty("open")] public string OpenPrice { get; set; }
	[JsonProperty("high")] public string HighPrice { get; set; }
	[JsonProperty("low")] public string LowPrice { get; set; }
	[JsonProperty("offerho")] public string AskPrice { get; set; }
	[JsonProperty("bidho")] public string BidPrice { get; set; }
	[JsonProperty("mdvolume")] public string SellVolume { get; set; }
	[JsonProperty("msvolume")] public string BuyVolume { get; set; }
	[JsonProperty("cgubun")] public string Aggressor { get; set; }
	[JsonProperty("status")] public string Status { get; set; }
	[JsonProperty("exchname")] public string ExchangeName { get; set; }
}

internal sealed class LsRealtimeDepth
{
	[JsonProperty("shcode")] public string Code { get; set; }
	[JsonProperty("ex_shcode")] public string ExchangeCode { get; set; }
	[JsonProperty("hotime")] public string Time { get; set; }
	[JsonProperty("offerho1")] public string AskPrice1 { get; set; }
	[JsonProperty("offerho2")] public string AskPrice2 { get; set; }
	[JsonProperty("offerho3")] public string AskPrice3 { get; set; }
	[JsonProperty("offerho4")] public string AskPrice4 { get; set; }
	[JsonProperty("offerho5")] public string AskPrice5 { get; set; }
	[JsonProperty("offerho6")] public string AskPrice6 { get; set; }
	[JsonProperty("offerho7")] public string AskPrice7 { get; set; }
	[JsonProperty("offerho8")] public string AskPrice8 { get; set; }
	[JsonProperty("offerho9")] public string AskPrice9 { get; set; }
	[JsonProperty("offerho10")] public string AskPrice10 { get; set; }
	[JsonProperty("unt_offerrem1")] public string AskVolume1 { get; set; }
	[JsonProperty("unt_offerrem2")] public string AskVolume2 { get; set; }
	[JsonProperty("unt_offerrem3")] public string AskVolume3 { get; set; }
	[JsonProperty("unt_offerrem4")] public string AskVolume4 { get; set; }
	[JsonProperty("unt_offerrem5")] public string AskVolume5 { get; set; }
	[JsonProperty("unt_offerrem6")] public string AskVolume6 { get; set; }
	[JsonProperty("unt_offerrem7")] public string AskVolume7 { get; set; }
	[JsonProperty("unt_offerrem8")] public string AskVolume8 { get; set; }
	[JsonProperty("unt_offerrem9")] public string AskVolume9 { get; set; }
	[JsonProperty("unt_offerrem10")] public string AskVolume10 { get; set; }
	[JsonProperty("bidho1")] public string BidPrice1 { get; set; }
	[JsonProperty("bidho2")] public string BidPrice2 { get; set; }
	[JsonProperty("bidho3")] public string BidPrice3 { get; set; }
	[JsonProperty("bidho4")] public string BidPrice4 { get; set; }
	[JsonProperty("bidho5")] public string BidPrice5 { get; set; }
	[JsonProperty("bidho6")] public string BidPrice6 { get; set; }
	[JsonProperty("bidho7")] public string BidPrice7 { get; set; }
	[JsonProperty("bidho8")] public string BidPrice8 { get; set; }
	[JsonProperty("bidho9")] public string BidPrice9 { get; set; }
	[JsonProperty("bidho10")] public string BidPrice10 { get; set; }
	[JsonProperty("unt_bidrem1")] public string BidVolume1 { get; set; }
	[JsonProperty("unt_bidrem2")] public string BidVolume2 { get; set; }
	[JsonProperty("unt_bidrem3")] public string BidVolume3 { get; set; }
	[JsonProperty("unt_bidrem4")] public string BidVolume4 { get; set; }
	[JsonProperty("unt_bidrem5")] public string BidVolume5 { get; set; }
	[JsonProperty("unt_bidrem6")] public string BidVolume6 { get; set; }
	[JsonProperty("unt_bidrem7")] public string BidVolume7 { get; set; }
	[JsonProperty("unt_bidrem8")] public string BidVolume8 { get; set; }
	[JsonProperty("unt_bidrem9")] public string BidVolume9 { get; set; }
	[JsonProperty("unt_bidrem10")] public string BidVolume10 { get; set; }
	[JsonProperty("unt_totofferrem")] public string TotalAskVolume { get; set; }
	[JsonProperty("unt_totbidrem")] public string TotalBidVolume { get; set; }

	public QuoteChange[] GetBids()
		=> CreateDepth(
			[BidPrice1, BidPrice2, BidPrice3, BidPrice4, BidPrice5, BidPrice6, BidPrice7, BidPrice8, BidPrice9, BidPrice10],
			[BidVolume1, BidVolume2, BidVolume3, BidVolume4, BidVolume5, BidVolume6, BidVolume7, BidVolume8, BidVolume9, BidVolume10]);

	public QuoteChange[] GetAsks()
		=> CreateDepth(
			[AskPrice1, AskPrice2, AskPrice3, AskPrice4, AskPrice5, AskPrice6, AskPrice7, AskPrice8, AskPrice9, AskPrice10],
			[AskVolume1, AskVolume2, AskVolume3, AskVolume4, AskVolume5, AskVolume6, AskVolume7, AskVolume8, AskVolume9, AskVolume10]);

	private static QuoteChange[] CreateDepth(string[] prices, string[] volumes)
		=> [.. prices.Select((price, index) => new QuoteChange(price.ToDecimal(), volumes[index].ToDecimal()))
			.Where(quote => quote.Price > 0)];
}

internal sealed class LsRealtimeOrder
{
	[JsonProperty("ordno")] public string OrderNumber { get; set; }
	[JsonProperty("orgordno")] public string OriginalOrderNumber { get; set; }
	[JsonProperty("shtcode")] public string SecurityCode { get; set; }
	[JsonProperty("shtnIsuno")] public string ShortSecurityCode { get; set; }
	[JsonProperty("hname")] public string SecurityName { get; set; }
	[JsonProperty("Isunm")] public string IssueName { get; set; }
	[JsonProperty("accno")] public string AccountNumber { get; set; }
	[JsonProperty("ordacntno")] public string OrderAccountNumber { get; set; }
	[JsonProperty("bnstp")] public string Side { get; set; }
	[JsonProperty("ordqty")] public string OrderQuantity { get; set; }
	[JsonProperty("ordprice")] public string OrderPrice { get; set; }
	[JsonProperty("ordprc")] public string Price { get; set; }
	[JsonProperty("unercqty")] public string Balance { get; set; }
	[JsonProperty("execqty")] public string ExecutionQuantity { get; set; }
	[JsonProperty("execprc")] public string ExecutionPrice { get; set; }
	[JsonProperty("execno")] public string ExecutionNumber { get; set; }
	[JsonProperty("ordtm")] public string OrderTime { get; set; }
	[JsonProperty("exectime")] public string ExecutionTime { get; set; }
	[JsonProperty("ordprcptncode")] public string PriceType { get; set; }
	[JsonProperty("ordcndi")] public string ConditionType { get; set; }
	[JsonProperty("msgcode")] public string MessageCode { get; set; }
}

internal sealed class LsSocketTextBody
{
	[JsonProperty("rsp_cd")] public string ResponseCode { get; set; }
	[JsonProperty("rsp_msg")] public string ResponseMessage { get; set; }
	[JsonProperty("msgcode")] public string MessageCode { get; set; }
	[JsonProperty("msg")]
	public string Message { get; set; }
}
