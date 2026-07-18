namespace StockSharp.Toobit.Native.Model;

sealed class ToobitWsHeader
{
	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("ping")]
	public long? Ping { get; set; }

	[JsonProperty("pong")]
	public long? Pong { get; set; }

	[JsonProperty("code")]
	public int? Code { get; set; }

	[JsonProperty("msg")]
	public string Message { get; set; }
}

sealed class ToobitWsEnvelope<TData>
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("symbolName")]
	public string SymbolName { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("params")]
	public ToobitWsResponseParameters Parameters { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }

	[JsonProperty("f")]
	public bool IsInitial { get; set; }

	[JsonProperty("sendTime")]
	public long? SendTime { get; set; }
}

sealed class ToobitWsResponseParameters
{
	[JsonProperty("realtimeInterval")]
	public string RealtimeInterval { get; set; }

	[JsonProperty("klineType")]
	public string KlineType { get; set; }

	[JsonProperty("binary")]
	public string IsBinary { get; set; }
}

sealed class ToobitWsTrade
{
	[JsonProperty("v")]
	public string TradeId { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("m")]
	public bool IsBuy { get; set; }
}

sealed class ToobitWsTicker
{
	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string LastPrice { get; set; }

	[JsonProperty("h")]
	public string HighPrice { get; set; }

	[JsonProperty("l")]
	public string LowPrice { get; set; }

	[JsonProperty("o")]
	public string OpenPrice { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }

	[JsonProperty("qv")]
	public string QuoteVolume { get; set; }

	[JsonProperty("e")]
	public long? LastTradeId { get; set; }
}

sealed class ToobitWsCandle
{
	[JsonProperty("t")]
	public long OpenTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string Close { get; set; }

	[JsonProperty("h")]
	public string High { get; set; }

	[JsonProperty("l")]
	public string Low { get; set; }

	[JsonProperty("o")]
	public string Open { get; set; }

	[JsonProperty("v")]
	public string Volume { get; set; }
}

sealed class ToobitWsDepth
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Time { get; set; }

	[JsonProperty("v")]
	public string Version { get; set; }

	[JsonProperty("b")]
	public string[][] Bids { get; set; }

	[JsonProperty("a")]
	public string[][] Asks { get; set; }
}

sealed class ToobitWsSubscriptionRequest
{
	[JsonProperty("symbol")]
	public string Symbol { get; set; }

	[JsonProperty("topic")]
	public string Topic { get; set; }

	[JsonProperty("event")]
	public ToobitWsEvents Event { get; set; }

	[JsonProperty("params")]
	public ToobitWsSubscriptionParameters Parameters { get; set; } = new();
}

sealed class ToobitWsSubscriptionParameters
{
	[JsonProperty("limit", NullValueHandling = NullValueHandling.Ignore)]
	public int? Limit { get; set; }

	[JsonProperty("binary")]
	public bool IsBinary { get; set; }
}

sealed class ToobitWsPing
{
	[JsonProperty("ping")]
	public long Time { get; set; }
}

sealed class ToobitWsPong
{
	[JsonProperty("pong")]
	public long Time { get; set; }
}

sealed class ToobitUserEventHeader
{
	[JsonProperty("e")]
	public string Event { get; set; }
}

sealed class ToobitListenKeyExpiry
{
	[JsonProperty("eventTime")]
	public long EventTime { get; set; }

	[JsonProperty("eventType")]
	public string EventType { get; set; }

	[JsonProperty("listenKey")]
	public string ListenKey { get; set; }
}

sealed class ToobitUserBalanceEvent
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public string EventTime { get; set; }

	[JsonProperty("B")]
	public ToobitUserBalance[] Balances { get; set; }
}

sealed class ToobitUserBalance
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("f")]
	public string Free { get; set; }

	[JsonProperty("l")]
	public string Locked { get; set; }
}

sealed class ToobitUserPositionEvent
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public string EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("S")]
	public ToobitPositionSides Side { get; set; }

	[JsonProperty("p")]
	public string AveragePrice { get; set; }

	[JsonProperty("P")]
	public string Position { get; set; }

	[JsonProperty("a")]
	public string Available { get; set; }

	[JsonProperty("f")]
	public string LiquidationPrice { get; set; }

	[JsonProperty("m")]
	public string Margin { get; set; }

	[JsonProperty("r")]
	public string RealizedPnl { get; set; }

	[JsonProperty("mt")]
	public ToobitMarginTypes MarginType { get; set; }

	[JsonProperty("up")]
	public string UnrealizedPnl { get; set; }

	[JsonProperty("v")]
	public string Leverage { get; set; }
}

sealed class ToobitUserOrderEvent
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public string EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("C")]
	public bool? IsClose { get; set; }

	[JsonProperty("S")]
	public ToobitOrderSides Side { get; set; }

	[JsonProperty("o")]
	public ToobitOrderTypes Type { get; set; }

	[JsonProperty("f")]
	public ToobitTimeInForce? TimeInForce { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("pt")]
	public ToobitPriceTypes? PriceType { get; set; }

	[JsonProperty("X")]
	public ToobitOrderStatuses Status { get; set; }

	[JsonProperty("i")]
	public string OrderId { get; set; }

	[JsonProperty("z")]
	public string ExecutedQuantity { get; set; }

	[JsonProperty("O")]
	public string CreationTime { get; set; }

	[JsonProperty("U")]
	public string UpdateTime { get; set; }

	[JsonProperty("v")]
	public string Leverage { get; set; }
}

sealed class ToobitUserTradeEvent
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public string EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("q")]
	public string Quantity { get; set; }

	[JsonProperty("t")]
	public string Time { get; set; }

	[JsonProperty("p")]
	public string Price { get; set; }

	[JsonProperty("T")]
	public string TicketId { get; set; }

	[JsonProperty("o")]
	public string OrderId { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("m")]
	public bool IsMaker { get; set; }

	[JsonProperty("S")]
	public ToobitOrderSides Side { get; set; }
}
