namespace StockSharp.Kucoin.Native;

interface INativeAdapter : IConnection, IDisposable
{
	ValueTask TimeAsync(CancellationToken cancellationToken);

	IAsyncEnumerable<SecurityMessage> SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
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

abstract class BaseNativeAdapter : Disposable, INativeAdapter
{
	private readonly Func<Message, CancellationToken, ValueTask> _outMessage;

	protected BaseNativeAdapter(KucoinMessageAdapter adapter, string boardCode, Func<Message, CancellationToken, ValueTask> outMessage)
    {
		Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		BoardCode = boardCode.ThrowIfEmpty(nameof(boardCode));
		_outMessage = outMessage ?? throw new ArgumentNullException(nameof(outMessage));
	}

	protected KucoinMessageAdapter Adapter { get; }
	protected string BoardCode { get; }

	protected static string GetSymbol(SecurityId securityId)
		=> securityId.SecurityCode.ToUpperInvariant();

	protected SecurityId GetSecId(string symbol)
	{
		return new()
		{
			SecurityCode = symbol.ToUpperInvariant(),
			BoardCode = BoardCode,
		};
	}

	protected DateTime CurrentTime => Adapter.CurrentTime;

	protected ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
		=> _outMessage(message, cancellationToken);

	protected ValueTask SendOutErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutMessageAsync(error.ToErrorMessage(), cancellationToken);

	protected ValueTask SendSubscriptionResultAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
		=> SendOutMessageAsync(message.CreateResult(), cancellationToken);

	protected ValueTask SendSubscriptionFinishedAsync(long transId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new SubscriptionFinishedMessage { OriginalTransactionId = transId }, cancellationToken);

	protected ValueTask SendSubscriptionReplyAsync(long originalTransactionId, CancellationToken cancellationToken, Exception error = null)
		=> SendOutMessageAsync(originalTransactionId.CreateSubscriptionResponse(error), cancellationToken);

	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public abstract ValueTask ConnectAsync(CancellationToken cancellationToken);
	public abstract void Disconnect();

	protected ValueTask SendOutConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		return StateChanged?.Invoke(state, cancellationToken) ?? default;
	}

	public abstract ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	public abstract IAsyncEnumerable<SecurityMessage> SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TimeAsync(CancellationToken cancellationToken);
}