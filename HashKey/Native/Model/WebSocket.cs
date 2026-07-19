namespace StockSharp.HashKey.Native.Model;

enum HashKeyWsTopics
{
	[EnumMember(Value = "kline")]
	Kline,

	[EnumMember(Value = "realtimes")]
	Realtimes,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "depth")]
	Depth,

	[EnumMember(Value = "bbo")]
	BestBidOffer,
}

enum HashKeyWsEvents
{
	[EnumMember(Value = "sub")]
	Subscribe,

	[EnumMember(Value = "cancel")]
	Cancel,
}

enum HashKeyPrivateEventTypes
{
	[EnumMember(Value = "outboundAccountInfo")]
	SpotAccount,

	[EnumMember(Value = "outboundContractAccountInfo")]
	FuturesAccount,

	[EnumMember(Value = "outboundCustodyAccountInfo")]
	CustodyAccount,

	[EnumMember(Value = "outboundFiatAccountInfo")]
	FiatAccount,

	[EnumMember(Value = "outboundOptAccountInfo")]
	OptionsAccount,

	[EnumMember(Value = "executionReport")]
	SpotOrder,

	[EnumMember(Value = "contractExecutionReport")]
	FuturesOrder,

	[EnumMember(Value = "ticketInfo")]
	Ticket,

	[EnumMember(Value = "outboundContractPositionInfo")]
	FuturesPosition,
}

sealed class HashKeyWsSubscriptionParams
{
	[JsonProperty("symbol")]
	public string Symbol { get; init; }

	[JsonProperty("klineType")]
	public string KlineType { get; init; }
}

sealed class HashKeyWsSubscriptionRequest
{
	[JsonProperty("topic")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyWsTopics Topic { get; init; }

	[JsonProperty("event")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyWsEvents Event { get; init; }

	[JsonProperty("params")]
	public HashKeyWsSubscriptionParams Params { get; init; }
}

sealed class HashKeyWsPingRequest
{
	[JsonProperty("ping")]
	public long Timestamp { get; init; }
}

sealed class HashKeyWsPongRequest
{
	[JsonProperty("pong")]
	public long Timestamp { get; init; }
}

sealed class HashKeyWsPublicHeader
{
	[JsonProperty("topic")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyWsTopics? Topic { get; set; }

	[JsonProperty("ping")]
	public long? Ping { get; set; }

	[JsonProperty("pong")]
	public long? Pong { get; set; }
}

sealed class HashKeyWsEnvelope<TData>
{
	[JsonProperty("topic")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyWsTopics Topic { get; set; }

	[JsonProperty("params")]
	public HashKeyWsSubscriptionParams Params { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class HashKeyWsTrade
{
	[JsonProperty("v")]
	public string Id { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}

sealed class HashKeyWsDepth
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("v")]
	public string Version { get; set; }

	[JsonProperty("b")]
	public HashKeyPriceLevel[] Bids { get; set; }

	[JsonProperty("a")]
	public HashKeyPriceLevel[] Asks { get; set; }
}

sealed class HashKeyWsBookTicker
{
	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public decimal Bid { get; set; }

	[JsonProperty("bz")]
	public decimal BidSize { get; set; }

	[JsonProperty("a")]
	public decimal Ask { get; set; }

	[JsonProperty("az")]
	public decimal AskSize { get; set; }

	[JsonProperty("t")]
	public long Timestamp { get; set; }
}

sealed class HashKeyWsRealtime
{
	[JsonProperty("t")]
	public long Timestamp { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("qv")]
	public decimal QuoteVolume { get; set; }
}

sealed class HashKeyWsKline
{
	[JsonProperty("t")]
	public long OpenTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }
}

sealed class HashKeyPrivateEventHeader
{
	[JsonProperty("e")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPrivateEventTypes Event { get; set; }
}

sealed class HashKeyWsBalance
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("f")]
	public decimal Free { get; set; }

	[JsonProperty("l")]
	public decimal Locked { get; set; }
}

sealed class HashKeyWsAccountUpdate
{
	[JsonProperty("e")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPrivateEventTypes Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("B")]
	public HashKeyWsBalance[] Balances { get; set; }
}

sealed class HashKeyWsOrderUpdate
{
	[JsonProperty("e")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPrivateEventTypes Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("S")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; set; }

	[JsonProperty("o")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderTypes Type { get; set; }

	[JsonProperty("f")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyTimeInForces? TimeInForce { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("X")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderStatuses Status { get; set; }

	[JsonProperty("i")]
	public string OrderId { get; set; }

	[JsonProperty("l")]
	public decimal LastQuantity { get; set; }

	[JsonProperty("z")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("L")]
	public decimal LastPrice { get; set; }

	[JsonProperty("n")]
	public decimal Commission { get; set; }

	[JsonProperty("N")]
	public string CommissionAsset { get; set; }

	[JsonProperty("m")]
	public bool IsMaker { get; set; }

	[JsonProperty("O")]
	public long CreationTime { get; set; }

	[JsonProperty("Z")]
	public decimal CumulativeQuoteQuantity { get; set; }

	[JsonProperty("v")]
	public decimal Leverage { get; set; }

	[JsonProperty("d")]
	public string ExecutionId { get; set; }

	[JsonProperty("r")]
	public decimal RemainingQuantity { get; set; }

	[JsonProperty("V")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("P")]
	public decimal IndexPrice { get; set; }

	[JsonProperty("x")]
	public string RejectReason { get; set; }
}

sealed class HashKeyWsTicket
{
	[JsonProperty("e")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPrivateEventTypes Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("t")]
	public long MatchTime { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("T")]
	public string TicketId { get; set; }

	[JsonProperty("o")]
	public string OrderId { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("a")]
	public string AccountId { get; set; }

	[JsonProperty("m")]
	public bool IsMaker { get; set; }

	[JsonProperty("S")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyOrderSides Side { get; set; }
}

sealed class HashKeyWsPosition
{
	[JsonProperty("e")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPrivateEventTypes Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("A")]
	public string AccountId { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("S")]
	[JsonConverter(typeof(StringEnumConverter))]
	public HashKeyPositionSides Side { get; set; }

	[JsonProperty("p")]
	public decimal AveragePrice { get; set; }

	[JsonProperty("P")]
	public decimal Position { get; set; }

	[JsonProperty("a")]
	public decimal Available { get; set; }

	[JsonProperty("f")]
	public decimal LiquidationPrice { get; set; }

	[JsonProperty("m")]
	public decimal Margin { get; set; }

	[JsonProperty("r")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("up")]
	public decimal UnrealizedPnL { get; set; }

	[JsonProperty("pv")]
	public decimal PositionValue { get; set; }

	[JsonProperty("v")]
	public decimal Leverage { get; set; }

	[JsonProperty("mt")]
	public string MarginType { get; set; }

	[JsonProperty("u")]
	public long UpdateTime { get; set; }
}
