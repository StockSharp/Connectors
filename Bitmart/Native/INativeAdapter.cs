namespace StockSharp.Bitmart.Native;

interface INativeAdapter
{
	event Func<Message, CancellationToken, ValueTask> OutMessage;

	ValueTask ConnectAsync(string address, bool isMarketData, bool isTransactional, int attemptsCount, WorkingTime workingTime, CancellationToken cancellationToken);
	ValueTask DisconnectAsync(CancellationToken cancellationToken);
	ValueTask ResetAsync(CancellationToken cancellationToken);
	ValueTask TimeAsync(CancellationToken cancellationToken);

	ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);

	ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);

	ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
}

abstract class BaseNativeAdapter : INativeAdapter
{
	public event Func<Message, CancellationToken, ValueTask> OutMessage;

	protected ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
	{
		if (OutMessage is { } handler)
			return handler(message, cancellationToken);
		return default;
	}

	protected ValueTask SendOutErrorAsync(Exception error, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(error.ToErrorMessage(), cancellationToken);
	}

	protected ValueTask SendSubscriptionResultAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(message.CreateResult(), cancellationToken);
	}

	protected ValueTask SendSubscriptionReplyAsync(long originalTransactionId, CancellationToken cancellationToken, Exception error = null)
	{
		return SendOutMessageAsync(originalTransactionId.CreateSubscriptionResponse(error), cancellationToken);
	}

	protected ValueTask SessionOnPusherErrorAsync(Exception error, CancellationToken cancellationToken) => SendOutErrorAsync(error, cancellationToken);
	protected ValueTask SendOutConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state.ToMessage() is Message msg)
			return SendOutMessageAsync(msg, cancellationToken);
		return default;
	}

	public abstract ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask ConnectAsync(string address, bool isMarketData, bool isTransactional, int attemptsCount, WorkingTime workingTime, CancellationToken cancellationToken);
	public abstract ValueTask DisconnectAsync(CancellationToken cancellationToken);
	public abstract ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	public abstract ValueTask ResetAsync(CancellationToken cancellationToken);
	public abstract ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TimeAsync(CancellationToken cancellationToken);
}
