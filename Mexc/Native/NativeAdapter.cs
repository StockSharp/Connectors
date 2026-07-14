namespace StockSharp.Mexc.Native;

abstract class NativeAdapter : BaseLogReceiver, IConnection
{
    protected NativeAdapter(Authenticator authenticator, IdGenerator transIdGen, string boardCode, SecurityTypes secType)
    {
		Authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
		TransIdGen = transIdGen ?? throw new ArgumentNullException(nameof(transIdGen));
		SecType = secType;
		BoardCode = boardCode.ThrowIfEmpty(nameof(boardCode));
		PortfolioName = $"{nameof(Mexc)}_{Authenticator.Key.ToId()}_{BoardCode}";
	}

	public SecurityTypes SecType { get; }
	public string BoardCode { get; }

	protected Authenticator Authenticator { get; }
	protected IdGenerator TransIdGen { get; }

	protected long GetNextId() => TransIdGen.GetNextId();

	protected string PortfolioName { get; }

	public event Func<Message, CancellationToken, ValueTask> NewOutMessage;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
	{
		if (NewOutMessage is { } handler)
			return handler(message, cancellationToken);
		return default;
	}

	protected ValueTask SendOutErrorAsync(Exception ex, CancellationToken cancellationToken)
		=> SendOutMessageAsync(ex.ToErrorMessage(), cancellationToken);

	protected ValueTask SendSubscriptionResultAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
		=> SendOutMessageAsync(message.CreateResult(), cancellationToken);

	protected ValueTask SendSubscriptionFinishedAsync(long originalTransactionId, CancellationToken cancellationToken, DateTime? nextFrom = null)
		=> SendOutMessageAsync(new SubscriptionFinishedMessage { OriginalTransactionId = originalTransactionId, NextFrom = nextFrom }, cancellationToken);

	protected ValueTask SendSubscriptionReplyAsync(long originalTransactionId, CancellationToken cancellationToken, Exception error = null)
		=> SendOutMessageAsync(originalTransactionId.CreateSubscriptionResponse(error), cancellationToken);

	protected ValueTask SendSubscriptionNotSupportedAsync(long originalTransactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(originalTransactionId.CreateNotSupported(), cancellationToken);

	protected ValueTask SendOutConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		return StateChanged?.Invoke(state, cancellationToken) ?? default;
	}

	public abstract ValueTask ConnectAsync(CancellationToken cancellationToken);
	public abstract void Disconnect();

	public abstract IAsyncEnumerable<SecurityMessage> SecurityLookup(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);

	public abstract ValueTask TFCandles(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask Ticks(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask Level1(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderBook(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrder(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	public abstract ValueTask ReplaceOrder(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrder(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelGroupOrder(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookup(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatus(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
}
