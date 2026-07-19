namespace StockSharp.CoinsPh.Native.Model;

enum CoinsPhSocketCommands
{
	[EnumMember(Value = "SUBSCRIBE")]
	Subscribe,

	[EnumMember(Value = "UNSUBSCRIBE")]
	Unsubscribe,
}

sealed class CoinsPhSubscriptionCommand
{
	[JsonProperty("method")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhSocketCommands Method { get; init; }

	[JsonProperty("params")]
	public string[] Streams { get; init; }

	[JsonProperty("id")]
	public long Id { get; init; }
}

sealed class CoinsPhPingCommand
{
	[JsonProperty("ping")]
	public long Timestamp { get; init; }
}

sealed class CoinsPhPublicSocketMessage
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("q")]
	public decimal Quantity { get; set; }

	[JsonProperty("T")]
	public long TradeTime { get; set; }

	[JsonProperty("m")]
	public bool IsBuyerMaker { get; set; }

	[JsonProperty("P")]
	public decimal PriceChangePercent { get; set; }

	[JsonProperty("w")]
	public decimal WeightedAveragePrice { get; set; }

	[JsonProperty("c")]
	public decimal LastPrice { get; set; }

	[JsonProperty("Q")]
	public decimal LastQuantity { get; set; }

	[JsonProperty("b")]
	public decimal BidPrice { get; set; }

	[JsonProperty("B")]
	public decimal BidQuantity { get; set; }

	[JsonProperty("a")]
	public decimal AskPrice { get; set; }

	[JsonProperty("A")]
	public decimal AskQuantity { get; set; }

	[JsonProperty("o")]
	public decimal OpenPrice { get; set; }

	[JsonProperty("h")]
	public decimal HighPrice { get; set; }

	[JsonProperty("l")]
	public decimal LowPrice { get; set; }

	[JsonProperty("v")]
	public decimal Volume { get; set; }

	[JsonProperty("O")]
	public long OpenTime { get; set; }

	[JsonProperty("C")]
	public long CloseTime { get; set; }

	[JsonProperty("n")]
	public long TradeCount { get; set; }

	[JsonProperty("lastUpdateId")]
	public long LastUpdateId { get; set; }

	[JsonProperty("bids")]
	public CoinsPhBookLevel[] Bids { get; set; }

	[JsonProperty("asks")]
	public CoinsPhBookLevel[] Asks { get; set; }

	[JsonProperty("k")]
	public CoinsPhSocketKline Kline { get; set; }

	[JsonProperty("id")]
	public long? CommandId { get; set; }

	[JsonProperty("code")]
	public int? ErrorCode { get; set; }

	[JsonProperty("msg")]
	public string ErrorMessage { get; set; }

	[JsonProperty("pong")]
	public long? Pong { get; set; }
}

sealed class CoinsPhSocketKline
{
	[JsonProperty("t")]
	public long OpenTime { get; set; }

	[JsonProperty("T")]
	public long CloseTime { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("i")]
	public string Interval { get; set; }

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
	public long TradeCount { get; set; }

	[JsonProperty("x")]
	public bool IsClosed { get; set; }

	[JsonProperty("q")]
	public decimal QuoteVolume { get; set; }
}

sealed class CoinsPhUserStreamMessage
{
	[JsonProperty("e")]
	public string Event { get; set; }

	[JsonProperty("E")]
	public long EventTime { get; set; }

	[JsonProperty("u")]
	public long AccountUpdateTime { get; set; }

	[JsonProperty("B")]
	public CoinsPhStreamBalance[] Balances { get; set; }

	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("d")]
	public decimal BalanceDelta { get; set; }

	[JsonProperty("s")]
	public string Symbol { get; set; }

	[JsonProperty("c")]
	public string ClientOrderId { get; set; }

	[JsonProperty("S")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhSides Side { get; set; }

	[JsonProperty("o")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhOrderTypes OrderType { get; set; }

	[JsonProperty("f")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhTimeInForces TimeInForce { get; set; }

	[JsonProperty("q")]
	public decimal OriginalQuantity { get; set; }

	[JsonProperty("p")]
	public decimal Price { get; set; }

	[JsonProperty("P")]
	public decimal StopPrice { get; set; }

	[JsonProperty("x")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhExecutionTypes ExecutionType { get; set; }

	[JsonProperty("X")]
	[JsonConverter(typeof(StringEnumConverter))]
	public CoinsPhOrderStatuses OrderStatus { get; set; }

	[JsonProperty("r")]
	public string RejectReason { get; set; }

	[JsonProperty("i")]
	public long OrderId { get; set; }

	[JsonProperty("l")]
	public decimal LastExecutedQuantity { get; set; }

	[JsonProperty("z")]
	public decimal CumulativeExecutedQuantity { get; set; }

	[JsonProperty("L")]
	public decimal LastExecutedPrice { get; set; }

	[JsonProperty("n")]
	public decimal Commission { get; set; }

	[JsonProperty("N")]
	public string CommissionAsset { get; set; }

	[JsonProperty("T")]
	public long TransactionTime { get; set; }

	[JsonProperty("t")]
	public long TradeId { get; set; }

	[JsonProperty("w")]
	public bool IsWorking { get; set; }

	[JsonProperty("m")]
	public bool IsMaker { get; set; }

	[JsonProperty("O")]
	public long OrderCreationTime { get; set; }

	[JsonProperty("Z")]
	public decimal CumulativeQuoteQuantity { get; set; }

	[JsonProperty("Y")]
	public decimal LastQuoteQuantity { get; set; }

	[JsonProperty("Q")]
	public decimal QuoteOrderQuantity { get; set; }
}

sealed class CoinsPhStreamBalance
{
	[JsonProperty("a")]
	public string Asset { get; set; }

	[JsonProperty("f")]
	public decimal Available { get; set; }

	[JsonProperty("l")]
	public decimal Locked { get; set; }
}
