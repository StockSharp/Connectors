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
	public const string Zero = "ZERO";
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
	public const string BidOffer = "BID_OFFER";
	public const string Trade = "TRADE";
	public const string InstrumentPositions = "INSTRUMENT_POSITIONS";
	public const string WalletBalances = "WALLET_BALANCES";
	public const string WorkingOrders = "WORKING_ORDERS";
	public const string Trades = "TRADES";
}

static class WsMessageTypes
{
	public const string Subscribe = "SUBSCRIBE";
	public const string Unsubscribe = "UNSUBSCRIBE";
	public const string Subscriptions = "SUBSCRIPTIONS";
	public const string SubscriptionRejection = "SUBSCRIPTION_REJECTION";
	public const string BidOfferSnapshot = "BID_OFFER_SNAPSHOT";
	public const string TradeEvent = "TRADE_EVENT";
	public const string InstrumentPosition = "INSTRUMENT_POSITION";
	public const string WalletBalances = "WALLET_BALANCES";
	public const string WorkingOrder = "WORKING_ORDER";
	public const string Trade = "TRADE";
	public const string Error = "ERROR";
}
