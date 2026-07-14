namespace StockSharp.ByBit.Native.Model;

class WebSocketResponse
{
	[JsonProperty("reqId")]
	public string ReqId { get; set; }

	[JsonProperty("req_id")]
	public string ReqId2 { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("op")]
	public string Op { get; set; }

	[JsonProperty("type")]
	public string Type { get; set; }

	[JsonProperty("data")]
	public JToken Data { get; set; }

	[JsonProperty("retCode")]
	public int RetCode { get; set; }

	[JsonProperty("retMsg")]
	public string RetMsg { get; set; }
}

class WebSocketOrderBookSnapshot
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public WebSocketOrderBookLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public WebSocketOrderBookLevel[] Asks { get; set; }

	[JsonProperty("u")]
	public long UpdateId { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }
}

[JsonConverter(typeof(JArrayToObjectConverter))]
class WebSocketOrderBookLevel
{
	public double Price { get; set; }
	public double Size { get; set; }
}

class WebSocketOrderBookDelta
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public WebSocketOrderBookLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public WebSocketOrderBookLevel[] Asks { get; set; }

	[JsonProperty("u")]
	public long UpdateId { get; set; }

	[JsonProperty("seq")]
	public long Sequence { get; set; }
}

class WebSocketTrade
{
	[JsonProperty("T")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }

	[JsonProperty("i")]
	public string TradeId { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public double Price { get; set; }

	[JsonProperty("v")]
	public double Volume { get; set; }

	[JsonProperty("S")]
	public string Side { get; set; }

	[JsonProperty("L")]
	public string PriceChange { get; set; }
}

class WebSocketKline
{
	[JsonProperty("start")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Start { get; set; }

	[JsonProperty("end")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime End { get; set; }

	[JsonProperty("interval")]
	public string Interval { get; set; }

	[JsonProperty("open")]
	public double Open { get; set; }

	[JsonProperty("close")]
	public double Close { get; set; }

	[JsonProperty("high")]
	public double High { get; set; }

	[JsonProperty("low")]
	public double Low { get; set; }

	[JsonProperty("volume")]
	public double Volume { get; set; }

	[JsonProperty("turnover")]
	public double Turnover { get; set; }

	[JsonProperty("confirm")]
	public bool Confirm { get; set; }

	[JsonProperty("timestamp")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime Time { get; set; }
}

class WebSocketExecution
{
	[JsonProperty("category")]
	public string Category { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("execFee")]
	public double? ExecFee { get; set; }

	[JsonProperty("execId")]
	public string ExecId { get; set; }

	[JsonProperty("execPrice")]
	public double? ExecPrice { get; set; }

	[JsonProperty("execQty")]
	public double? ExecQty { get; set; }

	[JsonProperty("execType")]
	public string ExecType { get; set; }

	[JsonProperty("execValue")]
	public double? ExecValue { get; set; }

	[JsonProperty("isMaker")]
	public bool IsMaker { get; set; }

	[JsonProperty("feeRate")]
	public double? FeeRate { get; set; }

	[JsonProperty("tradeIv")]
	public double? TradeIv { get; set; }

	[JsonProperty("markIv")]
	public double? MarkIv { get; set; }

	[JsonProperty("blockTradeId")]
	public string BlockTradeId { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public double? IndexPrice { get; set; }

	[JsonProperty("underlyingPrice")]
	public double? UnderlyingPrice { get; set; }

	[JsonProperty("leavesQty")]
	public double? LeavesQty { get; set; }

	[JsonProperty("orderId")]
	public string OrderId { get; set; }

	[JsonProperty("orderLinkId")]
	public string OrderLinkId { get; set; }

	[JsonProperty("orderPrice")]
	public double? OrderPrice { get; set; }

	[JsonProperty("orderQty")]
	public double? OrderQty { get; set; }

	[JsonProperty("orderType")]
	public string OrderType { get; set; }

	[JsonProperty("stopOrderType")]
	public string StopOrderType { get; set; }

	[JsonProperty("side")]
	public string Side { get; set; }

	[JsonProperty("execTime")]
	[JsonConverter(typeof(JsonDateTimeMlsConverter))]
	public DateTime? ExecTime { get; set; }

	[JsonProperty("isLeverage")]
	public string IsLeverage { get; set; }

	[JsonProperty("closedSize")]
	public double? ClosedSize { get; set; }

	[JsonProperty("seq")]
	public long Seq { get; set; }
}

class WebSocketTicker
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("bidPrice")]
	public double? BidPrice { get; set; }

	[JsonProperty("bid1Price")]
	public double? Bid1Price { get; set; }

	[JsonProperty("bidSize")]
	public double? BidSize { get; set; }

	[JsonProperty("bid1Size")]
	public double? Bid1Size { get; set; }

	[JsonProperty("bidIv")]
	public double? BidIv { get; set; }

	[JsonProperty("askPrice")]
	public double? AskPrice { get; set; }

	[JsonProperty("ask1Price")]
	public double? Ask1Price { get; set; }

	[JsonProperty("askSize")]
	public double? AskSize { get; set; }

	[JsonProperty("ask1Size")]
	public double? Ask1Size { get; set; }

	[JsonProperty("askIv")]
	public double? AskIv { get; set; }

	[JsonProperty("lastPrice")]
	public double? LastPrice { get; set; }

	[JsonProperty("tickDirection")]
	public string TickDirection { get; set; }

	[JsonProperty("highPrice24h")]
	public double? HighPrice24h { get; set; }

	[JsonProperty("lowPrice24h")]
	public double? LowPrice24h { get; set; }

	[JsonProperty("markPrice")]
	public double? MarkPrice { get; set; }

	[JsonProperty("indexPrice")]
	public double? IndexPrice { get; set; }

	[JsonProperty("markPriceIv")]
	public double? MarkPriceIv { get; set; }

	[JsonProperty("underlyingPrice")]
	public double? UnderlyingPrice { get; set; }

	[JsonProperty("openInterest")]
	public double? OpenInterest { get; set; }

	[JsonProperty("turnover24h")]
	public double? Turnover24h { get; set; }

	[JsonProperty("volume24h")]
	public double? Volume24h { get; set; }

	[JsonProperty("totalVolume")]
	public double? TotalVolume { get; set; }

	[JsonProperty("totalTurnover")]
	public double? TotalTurnover { get; set; }

	[JsonProperty("delta")]
	public double? Delta { get; set; }

	[JsonProperty("gamma")]
	public double? Gamma { get; set; }

	[JsonProperty("vega")]
	public double? Vega { get; set; }

	[JsonProperty("theta")]
	public double? Theta { get; set; }

	[JsonProperty("predictedDeliveryPrice")]
	public double? PredictedDeliveryPrice { get; set; }

	[JsonProperty("change24h")]
	public double? Change24h { get; set; }
}