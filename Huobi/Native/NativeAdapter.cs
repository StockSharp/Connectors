namespace StockSharp.Huobi.Native;

abstract class NativeAdapter(HuobiMessageAdapter parent, Authenticator authenticator, string domain)
{
	private readonly SynchronizedSet<long> _extraMdRequests = [];
	private readonly SynchronizedSet<long> _isAlgo = [];

    protected HuobiMessageAdapter Parent { get; } = parent ?? throw new ArgumentNullException(nameof(parent));
    protected Authenticator Authenticator { get; } = authenticator;
    protected string Domain { get; } = domain.ThrowIfEmpty(nameof(domain));

    public event Func<Message, CancellationToken, ValueTask> NewOutMessage;

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

	protected ValueTask SessionOnPusherError(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	protected ValueTask SendOutConnectionStateAsync(ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state.ToMessage() is Message msg)
			return SendOutMessageAsync(msg, cancellationToken);
		return default;
	}

	protected ValueTask SessionOnPing(string id, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new TimeMessage
		{
			TransactionId = Parent.TransactionIdGenerator.GetNextId(),
			OriginalTransactionId = id,
		}.LoopBack(Parent), cancellationToken);
	}

	protected long AddExtraRequest()
	{
		var extraId = Parent.TransactionIdGenerator.GetNextId();
		_extraMdRequests.Add(extraId);
		return extraId;
	}

	protected ValueTask SessionOnSubscriptionResponse(long originalTransactionId, CancellationToken cancellationToken)
	{
		if (_extraMdRequests.Contains(originalTransactionId))
			return default;

		return SendSubscriptionReplyAsync(originalTransactionId, cancellationToken);
	}

	protected DateTime CurrentTime => Parent.CurrentTime;

	protected bool IsAlgo(long transId) => _isAlgo.Contains(transId);
	protected void AddAlgo(long transId) => _isAlgo.Add(transId);

	protected string PortfolioName => nameof(Huobi) + "_" + Parent.Key.ToId();

	protected string EncodeAccountId(long accountId) => PortfolioName + "-" + accountId;
	protected long DecodeAccountId(string name) => name.Remove(PortfolioName + "-", true).To<long>();

	public async ValueTask ResetAsync(CancellationToken cancellationToken)
	{
		_extraMdRequests.Clear();
		_isAlgo.Clear();

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);

		await OnResetAsync(cancellationToken);
	}

	protected abstract ValueTask OnResetAsync(CancellationToken cancellationToken);
	public abstract ValueTask ConnectAsync(CancellationToken cancellationToken);
	public abstract ValueTask DisconnectAsync(CancellationToken cancellationToken);
	public abstract ValueTask TimeAsync(TimeMessage message, CancellationToken cancellationToken);
	public abstract ValueTask PortfolioLookupAsync(PortfolioLookupMessage message, CancellationToken cancellationToken);
	public abstract ValueTask OrderStatusAsync(OrderStatusMessage message, CancellationToken cancellationToken);
	public abstract ValueTask RegisterOrderAsync(OrderRegisterMessage message, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderAsync(OrderCancelMessage message, CancellationToken cancellationToken);
	public abstract ValueTask CancelOrderGroupAsync(OrderGroupCancelMessage message, CancellationToken cancellationToken);
	public abstract ValueTask SecurityLookupAsync(SecurityLookupMessage message, CancellationToken cancellationToken);
	public abstract ValueTask MarketDataAsync(MarketDataMessage message, CancellationToken cancellationToken);
	public abstract ValueTask SuspendedAsync(ProcessSuspendedMessage message, CancellationToken cancellationToken);
}
