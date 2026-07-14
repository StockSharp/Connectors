namespace StockSharp.Paradex.Native;

abstract class BaseNativeAdapter(ParadexMessageAdapter owner, string boardCode) : BaseLogReceiver, INativeAdapter
{
	protected ParadexMessageAdapter Owner { get; } = owner ?? throw new ArgumentNullException(nameof(owner));
	public string BoardCode { get; } = boardCode.ThrowIfEmpty(nameof(boardCode));

	public abstract ParadexSections Section { get; }

	protected abstract string SectionName { get; }

	public event Func<Message, CancellationToken, ValueTask> NewOutMessage;

	protected ValueTask SendOutMessageAsync(Message message, CancellationToken cancellationToken)
		=> NewOutMessage?.Invoke(message, cancellationToken) ?? default;

	protected ValueTask SendOutErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutMessageAsync(error.ToErrorMessage(), cancellationToken);

	protected ValueTask SendSubscriptionReplyAsync(long transactionId, CancellationToken cancellationToken, Exception error = null)
		=> SendOutMessageAsync(transactionId.CreateSubscriptionResponse(error), cancellationToken);

	protected ValueTask SendSubscriptionResultAsync(ISubscriptionMessage message, CancellationToken cancellationToken)
		=> SendOutMessageAsync(message.CreateResult(), cancellationToken);

	protected ValueTask SendSubscriptionFinishedAsync(long transactionId, CancellationToken cancellationToken)
		=> SendOutMessageAsync(new SubscriptionFinishedMessage { OriginalTransactionId = transactionId }, cancellationToken);

	protected new DateTime CurrentTime => DateTime.UtcNow;

	public virtual ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
		=> default;

	public virtual ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
		=> default;

	public virtual ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
		=> default;

	public virtual ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
		=> default;

	public abstract ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask Level1SubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask MarketDepthSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TicksSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask TFCandlesSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrderAsync(OrderRegisterMessage regMsg, CancellationToken cancellationToken);
	public abstract ValueTask ReplaceOrderAsync(OrderReplaceMessage replaceMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderAsync(OrderCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage cancelMsg, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken);
}
