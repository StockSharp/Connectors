namespace StockSharp.LMAX.Native.Model;

static class OrderTypes
{
	public const string Market = "MARKET";
	public const string Limit = "LIMIT";
	public const string Stop = "STOP";
	public const string StopLimit = "STOP_LIMIT";
}

static class OrderSides
{
	public const string Bid = "BID";
	public const string Ask = "ASK";
}

static class TimeInForce
{
	public const string FillOrKill = "FILL_OR_KILL";
	public const string ImmediateOrCancel = "IMMEDIATE_OR_CANCEL";
	public const string GoodForDay = "GOOD_FOR_DAY";
	public const string GoodTilCancelled = "GOOD_TIL_CANCELLED";
}

static class OrderBookStatus
{
	public const string Open = "OPEN";
	public const string Suspended = "SUSPENDED";
	public const string Closed = "CLOSED";
	public const string Settled = "SETTLED";
}

static class AssetClasses
{
	public const string Currency = "CURRENCY";
	public const string CurrencyFuture = "CURRENCY_FUTURE";
	public const string Commodity = "COMMODITY";
	public const string Equity = "EQUITY";
	public const string Index = "INDEX";
	public const string Ndf = "NDF";
	public const string Rate = "RATE";
}

static class TriggerMethods
{
	public const string OneTouch = "ONE_TOUCH";
	public const string BidOffer = "BID_OFFER";
}

static class Liquidity
{
	public const string Maker = "MAKER";
	public const string Taker = "TAKER";
}

static class WsChannels
{
	public const string OrderBook = "ORDER_BOOK";
	public const string Ticker = "TICKER";
	public const string Trade = "TRADE";
	public const string Order = "ORDER";
	public const string Execution = "EXECUTION";
	public const string Position = "POSITION";
	public const string Wallet = "WALLET";
	public const string Rejection = "REJECTION";
	public const string Heartbeat = "HEARTBEAT";
}

static class WsMessageTypes
{
	public const string Subscribe = "SUBSCRIBE";
	public const string Unsubscribe = "UNSUBSCRIBE";
	public const string Subscribed = "SUBSCRIBED";
	public const string Unsubscribed = "UNSUBSCRIBED";
	public const string Snapshot = "SNAPSHOT";
	public const string Update = "UPDATE";
	public const string Error = "ERROR";
	public const string Heartbeat = "HEARTBEAT";
}
