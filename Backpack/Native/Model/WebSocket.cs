namespace StockSharp.Backpack.Native.Model;

sealed class BackpackWsCommand
{
	[JsonProperty("method")]
	public BackpackWsMethods Method { get; init; }

	[JsonProperty("params")]
	public string[] Parameters { get; init; }

	[JsonProperty("signature")]
	public string[] Signature { get; init; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackWsMethods
{
	[EnumMember(Value = "SUBSCRIBE")]
	Subscribe,

	[EnumMember(Value = "UNSUBSCRIBE")]
	Unsubscribe,
}

sealed class BackpackWsEnvelopeHeader
{
	[JsonProperty("stream")]
	public string Stream { get; set; }

	[JsonProperty("error")]
	public BackpackWsError Error { get; set; }
}

sealed class BackpackWsError
{
	[JsonProperty("code")]
	public int Code { get; set; }

	[JsonProperty("message")]
	public string Message { get; set; }

	[JsonProperty("msg")]
	public string ShortMessage { get; set; }
}

sealed class BackpackWsEnvelope<TData>
{
	[JsonProperty("stream")]
	public string Stream { get; set; }

	[JsonProperty("data")]
	public TData Data { get; set; }
}

sealed class BackpackWsBookTicker
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("a")]
	public decimal? AskPrice { get; set; }

	[JsonProperty("A")]
	public decimal? AskQuantity { get; set; }

	[JsonProperty("b")]
	public decimal? BidPrice { get; set; }

	[JsonProperty("B")]
	public decimal? BidQuantity { get; set; }

	[JsonProperty("u")]
	public long UpdateId { get; set; }
}

sealed class BackpackWsDepth
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("a")]
	public BackpackPriceLevel[] Asks { get; set; }

	[JsonProperty("b")]
	public BackpackPriceLevel[] Bids { get; set; }

	[JsonProperty("U")]
	public long FirstUpdateId { get; set; }

	[JsonProperty("u")]
	public long LastUpdateId { get; set; }
}

sealed class BackpackWsTrade
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }
}

sealed class BackpackWsTicker
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("o")]
	public decimal FirstPrice { get; set; }

	[JsonProperty("c")]
	public decimal LastPrice { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("V")]
	public decimal QuoteVolume { get; set; }

	[JsonProperty("n")]
	public long Trades { get; set; }
}

sealed class BackpackWsKline
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public string StartTime { get; set; }

	[JsonProperty("T")]
	public string CloseTime { get; set; }

	[JsonProperty("o")]
	public decimal Open { get; set; }

	[JsonProperty("c")]
	public decimal Close { get; set; }

	[JsonProperty("h")]
	public decimal High { get; set; }

	[JsonProperty("l")]
	public decimal Low { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("n")]
	public long Trades { get; set; }

	[JsonProperty("X")]
	public bool IsClosed { get; set; }
}

sealed class BackpackWsMarkPrice
{
	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("p")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("f")]
	public decimal? FundingRate { get; set; }

	[JsonProperty("i")]
	public decimal? IndexPrice { get; set; }

	[JsonProperty("n")]
	public long? NextFundingTime { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackOrderEvents
{
	[EnumMember(Value = "orderAccepted")]
	Accepted,

	[EnumMember(Value = "orderCancelled")]
	Cancelled,

	[EnumMember(Value = "orderExpired")]
	Expired,

	[EnumMember(Value = "orderFill")]
	Fill,

	[EnumMember(Value = "orderModified")]
	Modified,

	[EnumMember(Value = "triggerPlaced")]
	TriggerPlaced,

	[EnumMember(Value = "triggerFailed")]
	TriggerFailed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackWsOrderTypes
{
	[EnumMember(Value = "MARKET")]
	Market,

	[EnumMember(Value = "LIMIT")]
	Limit,
}

sealed class BackpackWsOrderUpdate
{
	[JsonProperty("e")]
	public BackpackOrderEvents Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public uint? ClientId { get; set; }

	[JsonProperty("S")]
	public BackpackSides Side { get; set; }

	[JsonProperty("o")]
	public BackpackWsOrderTypes OrderType { get; set; }

	[JsonProperty("f")]
	public BackpackTimeInForces TimeInForce { get; set; }

	[JsonProperty("q")]
	public decimal? Quantity { get; set; }

	[JsonProperty("Q")]
	public decimal? QuoteQuantity { get; set; }

	[JsonProperty("p")]
	public decimal? Price { get; set; }

	[JsonProperty("r")]
	public bool IsReduceOnly { get; set; }

	[JsonProperty("X")]
	public BackpackOrderStatuses Status { get; set; }

	[JsonProperty("i")]
	public string OrderId { get; set; }

	[JsonProperty("t")]
	public long? TradeId { get; set; }

	[JsonProperty("l")]
	public decimal? FillQuantity { get; set; }

	[JsonProperty("z")]
	public decimal ExecutedQuantity { get; set; }

	[JsonProperty("Z")]
	public decimal ExecutedQuoteQuantity { get; set; }

	[JsonProperty("L")]
	public decimal? FillPrice { get; set; }

	[JsonProperty("m")]
	public bool? IsMaker { get; set; }

	[JsonProperty("n")]
	public decimal? Fee { get; set; }

	[JsonProperty("N")]
	public string FeeAsset { get; set; }

	[JsonProperty("I")]
	public string RelatedOrderId { get; set; }

	[JsonProperty("y")]
	public bool IsPostOnly { get; set; }

	[JsonProperty("R")]
	public string ExpiryReason { get; set; }
}

sealed class BackpackWsPositionUpdate
{
	[JsonProperty("e")]
	public BackpackPositionEvents? Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("T")]
	public long EngineTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("b")]
	public decimal BreakEvenPrice { get; set; }

	[JsonProperty("B")]
	public decimal EntryPrice { get; set; }

	[JsonProperty("f")]
	public decimal InitialMarginFraction { get; set; }

	[JsonProperty("M")]
	public decimal MarkPrice { get; set; }

	[JsonProperty("m")]
	public decimal MaintenanceMarginFraction { get; set; }

	[JsonProperty("q")]
	public decimal NetQuantity { get; set; }

	[JsonProperty("Q")]
	public decimal NetExposureQuantity { get; set; }

	[JsonProperty("n")]
	public decimal NetExposureNotional { get; set; }

	[JsonProperty("i")]
	public string PositionId { get; set; }

	[JsonProperty("p")]
	public decimal RealizedPnL { get; set; }

	[JsonProperty("P")]
	public decimal UnrealizedPnL { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
enum BackpackPositionEvents
{
	[EnumMember(Value = "positionAdjusted")]
	Adjusted,

	[EnumMember(Value = "positionOpened")]
	Opened,

	[EnumMember(Value = "positionClosed")]
	Closed,
}
