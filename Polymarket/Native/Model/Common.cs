namespace StockSharp.Polymarket.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSides
{
	[EnumMember(Value = "BUY")]
	Buy,

	[EnumMember(Value = "SELL")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketOrderTypes
{
	[EnumMember(Value = "GTC")]
	GoodTillCancelled,

	[EnumMember(Value = "GTD")]
	GoodTillDate,

	[EnumMember(Value = "FOK")]
	FillOrKill,

	[EnumMember(Value = "FAK")]
	FillAndKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketOrderStatuses
{
	[EnumMember(Value = "LIVE")]
	Live,

	[EnumMember(Value = "MATCHED")]
	Matched,

	[EnumMember(Value = "DELAYED")]
	Delayed,

	[EnumMember(Value = "UNMATCHED")]
	Unmatched,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "CANCELLED")]
	Cancelled,

	[EnumMember(Value = "FILLED")]
	Filled,

	[EnumMember(Value = "FAILED")]
	Failed,

	[EnumMember(Value = "REJECTED")]
	Rejected,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketTraderSides
{
	[EnumMember(Value = "TAKER")]
	Taker,

	[EnumMember(Value = "MAKER")]
	Maker,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketTradeStatuses
{
	[EnumMember(Value = "MATCHED")]
	Matched,

	[EnumMember(Value = "TRADE_STATUS_MATCHED")]
	TradeStatusMatched,

	[EnumMember(Value = "MATCHED_NOT_BROADCASTED")]
	MatchedNotBroadcasted,

	[EnumMember(Value = "TRADE_STATUS_MATCHED_NOT_BROADCASTED")]
	TradeStatusMatchedNotBroadcasted,

	[EnumMember(Value = "MINED")]
	Mined,

	[EnumMember(Value = "TRADE_STATUS_MINED")]
	TradeStatusMined,

	[EnumMember(Value = "CONFIRMED")]
	Confirmed,

	[EnumMember(Value = "TRADE_STATUS_CONFIRMED")]
	TradeStatusConfirmed,

	[EnumMember(Value = "RETRYING")]
	Retrying,

	[EnumMember(Value = "TRADE_STATUS_RETRYING")]
	TradeStatusRetrying,

	[EnumMember(Value = "FAILED")]
	Failed,

	[EnumMember(Value = "TRADE_STATUS_FAILED")]
	TradeStatusFailed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSocketEventTypes
{
	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "price_change")]
	PriceChange,

	[EnumMember(Value = "last_trade_price")]
	LastTradePrice,

	[EnumMember(Value = "tick_size_change")]
	TickSizeChange,

	[EnumMember(Value = "best_bid_ask")]
	BestBidAsk,

	[EnumMember(Value = "new_market")]
	NewMarket,

	[EnumMember(Value = "market_resolved")]
	MarketResolved,

	[EnumMember(Value = "order")]
	Order,

	[EnumMember(Value = "trade")]
	Trade,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSocketChannels
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "user")]
	User,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSocketOperations
{
	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSocketUserEventTypes
{
	[EnumMember(Value = "PLACEMENT")]
	Placement,

	[EnumMember(Value = "UPDATE")]
	Update,

	[EnumMember(Value = "CANCELLATION")]
	Cancellation,

	[EnumMember(Value = "TRADE")]
	Trade,
}

[JsonConverter(typeof(StringEnumConverter))]
enum PolymarketSocketStatuses
{
	[EnumMember(Value = "LIVE")]
	Live,

	[EnumMember(Value = "MATCHED")]
	Matched,

	[EnumMember(Value = "DELAYED")]
	Delayed,

	[EnumMember(Value = "UNMATCHED")]
	Unmatched,

	[EnumMember(Value = "CANCELED")]
	Canceled,

	[EnumMember(Value = "MATCHED_NOT_BROADCASTED")]
	MatchedNotBroadcasted,

	[EnumMember(Value = "MINED")]
	Mined,

	[EnumMember(Value = "CONFIRMED")]
	Confirmed,

	[EnumMember(Value = "RETRYING")]
	Retrying,

	[EnumMember(Value = "FAILED")]
	Failed,

	[EnumMember(Value = "TRADE_STATUS_MATCHED")]
	TradeStatusMatched,

	[EnumMember(Value = "TRADE_STATUS_MATCHED_NOT_BROADCASTED")]
	TradeStatusMatchedNotBroadcasted,

	[EnumMember(Value = "TRADE_STATUS_MINED")]
	TradeStatusMined,

	[EnumMember(Value = "TRADE_STATUS_CONFIRMED")]
	TradeStatusConfirmed,

	[EnumMember(Value = "TRADE_STATUS_RETRYING")]
	TradeStatusRetrying,

	[EnumMember(Value = "TRADE_STATUS_FAILED")]
	TradeStatusFailed,
}

sealed class PolymarketVersionResponse
{
	[JsonProperty("version")]
	public int Version { get; init; }
}

sealed class PolymarketMarketsPage
{
	[JsonProperty("limit")]
	public int Limit { get; init; }

	[JsonProperty("count")]
	public int Count { get; init; }

	[JsonProperty("next_cursor")]
	public string NextCursor { get; init; }

	[JsonProperty("data")]
	public PolymarketMarketDefinition[] Data { get; init; }
}

sealed class PolymarketMarketDefinition
{
	[JsonProperty("enable_order_book")]
	public bool IsOrderBookEnabled { get; init; }

	[JsonProperty("active")]
	public bool IsActive { get; init; }

	[JsonProperty("closed")]
	public bool IsClosed { get; init; }

	[JsonProperty("archived")]
	public bool IsArchived { get; init; }

	[JsonProperty("accepting_orders")]
	public bool IsAcceptingOrders { get; init; }

	[JsonProperty("accepting_order_timestamp")]
	public string AcceptingOrderTimestamp { get; init; }

	[JsonProperty("minimum_order_size")]
	public decimal MinimumOrderSize { get; init; }

	[JsonProperty("minimum_tick_size")]
	public decimal MinimumTickSize { get; init; }

	[JsonProperty("condition_id")]
	public string ConditionId { get; init; }

	[JsonProperty("question_id")]
	public string QuestionId { get; init; }

	[JsonProperty("question")]
	public string Question { get; init; }

	[JsonProperty("description")]
	public string Description { get; init; }

	[JsonProperty("market_slug")]
	public string MarketSlug { get; init; }

	[JsonProperty("end_date_iso")]
	public string EndDate { get; init; }

	[JsonProperty("game_start_time")]
	public string GameStartTime { get; init; }

	[JsonProperty("seconds_delay")]
	public int SecondsDelay { get; init; }

	[JsonProperty("maker_base_fee")]
	public decimal MakerBaseFee { get; init; }

	[JsonProperty("taker_base_fee")]
	public decimal TakerBaseFee { get; init; }

	[JsonProperty("neg_risk")]
	public bool IsNegativeRisk { get; init; }

	[JsonProperty("tokens")]
	public PolymarketTokenDefinition[] Tokens { get; init; }

	[JsonProperty("tags")]
	public string[] Tags { get; init; }
}

sealed class PolymarketTokenDefinition
{
	[JsonProperty("token_id")]
	public string TokenId { get; init; }

	[JsonProperty("outcome")]
	public string Outcome { get; init; }

	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("winner")]
	public bool IsWinner { get; init; }
}

sealed class PolymarketMarket
{
	public string SecurityCode { get; set; }
	public string TokenId { get; init; }
	public string ConditionId { get; init; }
	public string Question { get; init; }
	public string Description { get; init; }
	public string Slug { get; init; }
	public string Outcome { get; init; }
	public DateTime? ExpiryDate { get; init; }
	public decimal PriceStep { get; set; }
	public decimal MinimumVolume { get; init; }
	public decimal ReferencePrice { get; set; }
	public bool IsNegativeRisk { get; init; }
	public bool IsActive { get; init; }
}

sealed class PolymarketPriceHistoryResponse
{
	[JsonProperty("history")]
	public PolymarketPricePoint[] History { get; init; }
}

sealed class PolymarketPricePoint
{
	[JsonProperty("t")]
	public long Timestamp { get; init; }

	[JsonProperty("p")]
	public decimal Price { get; init; }
}

sealed class PolymarketLastTradePrice
{
	[JsonProperty("price")]
	public decimal Price { get; init; }

	[JsonProperty("side")]
	public PolymarketSides Side { get; init; }
}

sealed class PolymarketApiError
{
	[JsonProperty("error")]
	public string Error { get; init; }

	[JsonProperty("errorMsg")]
	public string ErrorMessage { get; init; }

	[JsonProperty("message")]
	public string Message { get; init; }
}
