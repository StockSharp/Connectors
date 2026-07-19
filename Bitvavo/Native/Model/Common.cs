namespace StockSharp.Bitvavo.Native.Model;

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoSides
{
	[EnumMember(Value = "buy")]
	Buy,

	[EnumMember(Value = "sell")]
	Sell,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoOrderTypes
{
	[EnumMember(Value = "market")]
	Market,

	[EnumMember(Value = "limit")]
	Limit,

	[EnumMember(Value = "stopLoss")]
	StopLoss,

	[EnumMember(Value = "stopLossLimit")]
	StopLossLimit,

	[EnumMember(Value = "takeProfit")]
	TakeProfit,

	[EnumMember(Value = "takeProfitLimit")]
	TakeProfitLimit,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoOrderStatuses
{
	[EnumMember(Value = "new")]
	New,

	[EnumMember(Value = "awaitingTrigger")]
	AwaitingTrigger,

	[EnumMember(Value = "canceled")]
	Canceled,

	[EnumMember(Value = "expired")]
	Expired,

	[EnumMember(Value = "filled")]
	Filled,

	[EnumMember(Value = "partiallyFilled")]
	PartiallyFilled,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoTimeInForces
{
	[EnumMember(Value = "GTC")]
	GoodTillCanceled,

	[EnumMember(Value = "IOC")]
	ImmediateOrCancel,

	[EnumMember(Value = "FOK")]
	FillOrKill,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoSelfTradePreventions
{
	[EnumMember(Value = "decrementAndCancel")]
	DecrementAndCancel,

	[EnumMember(Value = "cancelOldest")]
	CancelOldest,

	[EnumMember(Value = "cancelNewest")]
	CancelNewest,

	[EnumMember(Value = "cancelBoth")]
	CancelBoth,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoTriggerTypes
{
	[EnumMember(Value = "price")]
	Price,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoTriggerReferences
{
	[EnumMember(Value = "lastTrade")]
	LastTrade,

	[EnumMember(Value = "bestBid")]
	BestBid,

	[EnumMember(Value = "bestAsk")]
	BestAsk,

	[EnumMember(Value = "midPrice")]
	MidPrice,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoActions
{
	[EnumMember(Value = "authenticate")]
	Authenticate,

	[EnumMember(Value = "subscribe")]
	Subscribe,

	[EnumMember(Value = "unsubscribe")]
	Unsubscribe,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoChannels
{
	[EnumMember(Value = "ticker")]
	Ticker,

	[EnumMember(Value = "ticker24h")]
	Ticker24,

	[EnumMember(Value = "trades")]
	Trades,

	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "candles")]
	Candles,

	[EnumMember(Value = "account")]
	Account,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoEvents
{
	[EnumMember(Value = "ticker")]
	Ticker,

	[EnumMember(Value = "ticker24h")]
	Ticker24,

	[EnumMember(Value = "trade")]
	Trade,

	[EnumMember(Value = "book")]
	Book,

	[EnumMember(Value = "candles")]
	Candles,

	[EnumMember(Value = "order")]
	Order,

	[EnumMember(Value = "fill")]
	Fill,

	[EnumMember(Value = "subscribed")]
	Subscribed,

	[EnumMember(Value = "unsubscribed")]
	Unsubscribed,
}

[JsonConverter(typeof(StringEnumConverter))]
enum BitvavoMarketStatuses
{
	[EnumMember(Value = "trading")]
	Trading,

	[EnumMember(Value = "halted")]
	Halted,

	[EnumMember(Value = "auction")]
	Auction,

	[EnumMember(Value = "auctionMatching")]
	AuctionMatching,

	[EnumMember(Value = "cancelOnly")]
	CancelOnly,
}

readonly record struct BitvavoParameter(string Name, string Value);

interface IBitvavoQuery
{
	BitvavoParameter[] GetParameters();
}

sealed class BitvavoEmptyQuery : IBitvavoQuery
{
	public static BitvavoEmptyQuery Instance { get; } = new();

	private BitvavoEmptyQuery()
	{
	}

	public BitvavoParameter[] GetParameters() => [];
}

sealed class BitvavoError
{
	[JsonProperty("errorCode")]
	public int? ErrorCode { get; set; }

	[JsonProperty("error")]
	public string Error { get; set; }

	[JsonProperty("market")]
	public string Market { get; set; }
}
