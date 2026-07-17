namespace StockSharp.Flattrade.Native.Model;

class FlattradeResponse
{
	[JsonProperty("stat")]
	public string Status { get; set; }

	[JsonProperty("emsg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("request_time")]
	public string RequestTime { get; set; }
}

sealed class FlattradeUserRequest
{
	[JsonProperty("ordersource")]
	public string OrderSource { get; set; } = "API";

	[JsonProperty("uid")]
	public string UserId { get; set; }
}

sealed class FlattradeAccountRequest
{
	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("prd")]
	public string Product { get; set; }

	[JsonProperty("seg")]
	public string Segment { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }
}

sealed class FlattradeInstrumentRequest
{
	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }
}

sealed class FlattradeCandleRequest
{
	[JsonProperty("ordersource")]
	public string OrderSource { get; set; } = "API";

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("st")]
	public string From { get; set; }

	[JsonProperty("et")]
	public string To { get; set; }

	[JsonProperty("intrv")]
	public string Interval { get; set; }
}

sealed class FlattradeDailyCandleRequest
{
	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("sym")]
	public string Symbol { get; set; }

	[JsonProperty("from")]
	public string From { get; set; }

	[JsonProperty("to")]
	public string To { get; set; }
}

sealed class FlattradePlaceOrderRequest
{
	[JsonProperty("ordersource")]
	public string OrderSource { get; set; } = "API";

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("trantype")]
	public string Side { get; set; }

	[JsonProperty("prd")]
	public string Product { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("dscqty")]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("prctyp")]
	public string PriceType { get; set; }

	[JsonProperty("prc")]
	public string Price { get; set; }

	[JsonProperty("trgprc")]
	public string TriggerPrice { get; set; }

	[JsonProperty("ret")]
	public string Retention { get; set; }

	[JsonProperty("amo")]
	public string AfterMarket { get; set; }

	[JsonProperty("remarks")]
	public string Remarks { get; set; }

	[JsonProperty("blprc")]
	public string StopLossPrice { get; set; }

	[JsonProperty("bpprc")]
	public string ProfitPrice { get; set; }

	[JsonProperty("trailprc")]
	public string TrailingPrice { get; set; }
}

sealed class FlattradeModifyOrderRequest
{
	[JsonProperty("ordersource")]
	public string OrderSource { get; set; } = "API";

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("norenordno")]
	public string OrderId { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("prctyp")]
	public string PriceType { get; set; }

	[JsonProperty("prc")]
	public string Price { get; set; }

	[JsonProperty("trgprc")]
	public string TriggerPrice { get; set; }

	[JsonProperty("blprc")]
	public string StopLossPrice { get; set; }

	[JsonProperty("bpprc")]
	public string ProfitPrice { get; set; }

	[JsonProperty("trailprc")]
	public string TrailingPrice { get; set; }
}

sealed class FlattradeCancelOrderRequest
{
	[JsonProperty("ordersource")]
	public string OrderSource { get; set; } = "API";

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("norenordno")]
	public string OrderId { get; set; }
}

sealed class FlattradeOrderResult : FlattradeResponse
{
	[JsonProperty("norenordno")]
	public string OrderId { get; set; }

	[JsonProperty("result")]
	public string Result { get; set; }
}

class FlattradeOrder : FlattradeResponse
{
	[JsonProperty("norenordno")]
	public string OrderId { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("qty")]
	public string Quantity { get; set; }

	[JsonProperty("prc")]
	public string Price { get; set; }

	[JsonProperty("prd")]
	public string Product { get; set; }

	[JsonProperty("status")]
	public string OrderStatus { get; set; }

	[JsonProperty("reporttype")]
	public string ReportType { get; set; }

	[JsonProperty("trantype")]
	public string Side { get; set; }

	[JsonProperty("prctyp")]
	public string PriceType { get; set; }

	[JsonProperty("ret")]
	public string Retention { get; set; }

	[JsonProperty("fillshares")]
	public string FilledQuantity { get; set; }

	[JsonProperty("avgprc")]
	public string AveragePrice { get; set; }

	[JsonProperty("rejreason")]
	public string RejectionReason { get; set; }

	[JsonProperty("exchordid")]
	public string ExchangeOrderId { get; set; }

	[JsonProperty("cancelqty")]
	public string CancelledQuantity { get; set; }

	[JsonProperty("remarks")]
	public string Remarks { get; set; }

	[JsonProperty("dscqty")]
	public string DisclosedQuantity { get; set; }

	[JsonProperty("trgprc")]
	public string TriggerPrice { get; set; }

	[JsonProperty("amo")]
	public string AfterMarket { get; set; }

	[JsonProperty("bpprc")]
	public string ProfitPrice { get; set; }

	[JsonProperty("blprc")]
	public string StopLossPrice { get; set; }

	[JsonProperty("trailprc")]
	public string TrailingPrice { get; set; }

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("norentm")]
	public string NorenTime { get; set; }

	[JsonProperty("ordenttm")]
	public string OrderEntryTime { get; set; }

	[JsonProperty("exch_tm")]
	public string ExchangeTime { get; set; }

	[JsonProperty("fltm")]
	public string FillTime { get; set; }

	[JsonProperty("flid")]
	public string FillId { get; set; }

	[JsonProperty("flqty")]
	public string FillQuantity { get; set; }

	[JsonProperty("flprc")]
	public string FillPrice { get; set; }

	[JsonProperty("snonum")]
	public string ChildOrderId { get; set; }

	[JsonProperty("snoordt")]
	public string ChildOrderType { get; set; }
}

sealed class FlattradeTrade : FlattradeOrder
{
}

sealed class FlattradePosition : FlattradeResponse
{
	[JsonProperty("t")]
	public string Type { get; set; }

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("prd")]
	public string Product { get; set; }

	[JsonProperty("netqty")]
	public string NetQuantity { get; set; }

	[JsonProperty("netavgprc")]
	public string NetAveragePrice { get; set; }

	[JsonProperty("buyavgprc")]
	public string BuyAveragePrice { get; set; }

	[JsonProperty("sellavgprc")]
	public string SellAveragePrice { get; set; }

	[JsonProperty("totbuyavgprc")]
	public string TotalBuyAveragePrice { get; set; }

	[JsonProperty("totsellavgprc")]
	public string TotalSellAveragePrice { get; set; }

	[JsonProperty("lp")]
	public string LastPrice { get; set; }

	[JsonProperty("rpnl")]
	public string RealizedPnL { get; set; }

	[JsonProperty("urmtom")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("daybuyqty")]
	public string DayBuyQuantity { get; set; }

	[JsonProperty("daysellqty")]
	public string DaySellQuantity { get; set; }

	[JsonProperty("daybuyavgprc")]
	public string DayBuyAveragePrice { get; set; }

	[JsonProperty("daysellavgprc")]
	public string DaySellAveragePrice { get; set; }

	[JsonProperty("cfbuyqty")]
	public string CarryBuyQuantity { get; set; }

	[JsonProperty("cfsellqty")]
	public string CarrySellQuantity { get; set; }
}

sealed class FlattradeHolding : FlattradeResponse
{
	[JsonProperty("exch_tsym")]
	public FlattradeHoldingInstrument[] Instruments { get; set; }

	[JsonProperty("holdqty")]
	public string HoldingQuantity { get; set; }

	[JsonProperty("colqty")]
	public string CollateralQuantity { get; set; }

	[JsonProperty("btstqty")]
	public string BtstQuantity { get; set; }

	[JsonProperty("btstcolqty")]
	public string BtstCollateralQuantity { get; set; }

	[JsonProperty("usedqty")]
	public string UsedQuantity { get; set; }

	[JsonProperty("upldprc")]
	public string UploadPrice { get; set; }
}

sealed class FlattradeHoldingInstrument
{
	[JsonProperty("exch")]
	public string Exchange { get; set; }

	[JsonProperty("token")]
	public string Token { get; set; }

	[JsonProperty("tsym")]
	public string TradingSymbol { get; set; }
}

sealed class FlattradeLimits : FlattradeResponse
{
	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("cash")]
	public string Cash { get; set; }

	[JsonProperty("payin")]
	public string PayIn { get; set; }

	[JsonProperty("payout")]
	public string PayOut { get; set; }

	[JsonProperty("marginused")]
	public string MarginUsed { get; set; }

	[JsonProperty("rpnl")]
	public string RealizedPnL { get; set; }

	[JsonProperty("unmtom")]
	public string UnrealizedPnL { get; set; }

	[JsonProperty("collateral")]
	public string Collateral { get; set; }
}

sealed class FlattradeCandle : FlattradeResponse
{
	[JsonProperty("time")]
	public string Time { get; set; }

	[JsonProperty("ssboe")]
	public string EpochTime { get; set; }

	[JsonProperty("into")]
	public string Open { get; set; }

	[JsonProperty("inth")]
	public string High { get; set; }

	[JsonProperty("intl")]
	public string Low { get; set; }

	[JsonProperty("intc")]
	public string Close { get; set; }

	[JsonProperty("intv")]
	public string Volume { get; set; }

	[JsonProperty("oi")]
	public string OpenInterest { get; set; }
}

sealed class FlattradeInstrument
{
	public string Exchange { get; set; }
	public string Token { get; set; }
	public decimal LotSize { get; set; }
	public int Precision { get; set; }
	public decimal Multiplier { get; set; }
	public string Symbol { get; set; }
	public string TradingSymbol { get; set; }
	public DateTime? Expiry { get; set; }
	public string Instrument { get; set; }
	public string OptionType { get; set; }
	public decimal StrikePrice { get; set; }
	public decimal TickSize { get; set; }
}

sealed class FlattradeDepthLevel
{
	public decimal Price { get; set; }
	public decimal Volume { get; set; }
	public int OrdersCount { get; set; }
}

class FlattradeSocketEnvelope
{
	[JsonProperty("t")]
	public string Type { get; set; }
}

sealed class FlattradeSocketLoginRequest
{
	[JsonProperty("t")]
	public string Type { get; set; } = "a";

	[JsonProperty("uid")]
	public string UserId { get; set; }

	[JsonProperty("actid")]
	public string AccountId { get; set; }

	[JsonProperty("accesstoken")]
	public string AccessToken { get; set; }

	[JsonProperty("source")]
	public string Source { get; set; } = "API";
}

sealed class FlattradeSocketSubscriptionRequest
{
	[JsonProperty("t")]
	public string Type { get; set; }

	[JsonProperty("k")]
	public string Instruments { get; set; }
}

sealed class FlattradeSocketOrderRequest
{
	[JsonProperty("t")]
	public string Type { get; set; } = "o";

	[JsonProperty("actid")]
	public string AccountId { get; set; }
}

sealed class FlattradeSocketPositionRequest
{
	[JsonProperty("t")]
	public string Type { get; set; } = "p";

	[JsonProperty("actid")]
	public string AccountId { get; set; }
}

sealed class FlattradeSocketHeartbeat
{
	[JsonProperty("t")]
	public string Type { get; set; } = "h";
}

sealed class FlattradeSocketAcknowledgement : FlattradeSocketEnvelope
{
	[JsonProperty("s")]
	public string Status { get; set; }

	[JsonProperty("emsg")]
	public string ErrorMessage { get; set; }
}

sealed class FlattradeMarketUpdate : FlattradeSocketEnvelope
{
	[JsonProperty("e")]
	public string Exchange { get; set; }

	[JsonProperty("tk")]
	public string Token { get; set; }

	[JsonProperty("ts")]
	public string TradingSymbol { get; set; }

	[JsonProperty("pp")]
	public string Precision { get; set; }

	[JsonProperty("ti")]
	public string TickSize { get; set; }

	[JsonProperty("ls")]
	public string LotSize { get; set; }

	[JsonProperty("lp")]
	public string LastPrice { get; set; }

	[JsonProperty("ltq")]
	public string LastQuantity { get; set; }

	[JsonProperty("ltt")]
	public string LastTradeTime { get; set; }

	[JsonProperty("pc")]
	public string ChangePercent { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("o")]
	public string Open { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("c")]
	public string Close { get; set; }

	[JsonProperty("ap")]
	public string AveragePrice { get; set; }

	[JsonProperty("oi")]
	public string OpenInterest { get; set; }

	[JsonProperty("poi")]
	public string PreviousOpenInterest { get; set; }

	[JsonProperty("toi")]
	public string TotalOpenInterest { get; set; }

	[JsonProperty("tbq")]
	public string TotalBuyQuantity { get; set; }

	[JsonProperty("tsq")]
	public string TotalSellQuantity { get; set; }

	[JsonProperty("lc")]
	public string LowerCircuit { get; set; }

	[JsonProperty("uc")]
	public string UpperCircuit { get; set; }

	[JsonProperty("52h")]
	public string YearHigh { get; set; }

	[JsonProperty("52l")]
	public string YearLow { get; set; }

	[JsonProperty("ft")]
	public string FeedTime { get; set; }

	[JsonProperty("bp1")]
	public string BidPrice1 { get; set; }
	[JsonProperty("bq1")]
	public string BidQuantity1 { get; set; }
	[JsonProperty("bo1")]
	public string BidOrders1 { get; set; }
	[JsonProperty("sp1")]
	public string AskPrice1 { get; set; }
	[JsonProperty("sq1")]
	public string AskQuantity1 { get; set; }
	[JsonProperty("so1")]
	public string AskOrders1 { get; set; }

	[JsonProperty("bp2")]
	public string BidPrice2 { get; set; }
	[JsonProperty("bq2")]
	public string BidQuantity2 { get; set; }
	[JsonProperty("bo2")]
	public string BidOrders2 { get; set; }
	[JsonProperty("sp2")]
	public string AskPrice2 { get; set; }
	[JsonProperty("sq2")]
	public string AskQuantity2 { get; set; }
	[JsonProperty("so2")]
	public string AskOrders2 { get; set; }

	[JsonProperty("bp3")]
	public string BidPrice3 { get; set; }
	[JsonProperty("bq3")]
	public string BidQuantity3 { get; set; }
	[JsonProperty("bo3")]
	public string BidOrders3 { get; set; }
	[JsonProperty("sp3")]
	public string AskPrice3 { get; set; }
	[JsonProperty("sq3")]
	public string AskQuantity3 { get; set; }
	[JsonProperty("so3")]
	public string AskOrders3 { get; set; }

	[JsonProperty("bp4")]
	public string BidPrice4 { get; set; }
	[JsonProperty("bq4")]
	public string BidQuantity4 { get; set; }
	[JsonProperty("bo4")]
	public string BidOrders4 { get; set; }
	[JsonProperty("sp4")]
	public string AskPrice4 { get; set; }
	[JsonProperty("sq4")]
	public string AskQuantity4 { get; set; }
	[JsonProperty("so4")]
	public string AskOrders4 { get; set; }

	[JsonProperty("bp5")]
	public string BidPrice5 { get; set; }
	[JsonProperty("bq5")]
	public string BidQuantity5 { get; set; }
	[JsonProperty("bo5")]
	public string BidOrders5 { get; set; }
	[JsonProperty("sp5")]
	public string AskPrice5 { get; set; }
	[JsonProperty("sq5")]
	public string AskQuantity5 { get; set; }
	[JsonProperty("so5")]
	public string AskOrders5 { get; set; }
}
