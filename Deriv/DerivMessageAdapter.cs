namespace StockSharp.Deriv;

public partial class DerivMessageAdapter
{
	private enum DerivSubscriptionKinds
	{
		Level1,
		Ticks,
		Candles,
		Balance,
		Transaction,
		ContractOrder,
		ContractOrderStatus,
		ContractPortfolio,
	}

	private sealed class DerivSubscription
	{
		public string NativeKey { get; init; }
		public DerivSubscriptionKinds Kind { get; init; }
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Symbol { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public long ContractId { get; init; }
		public long Skip { get; init; }
		public long Count { get; init; }
		public DerivCandle LastCandle { get; set; }
	}

	private sealed class DerivOrderTracker
	{
		public long TransactionId { get; init; }
		public long ContractId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public decimal Volume { get; init; }
		public decimal BuyPrice { get; init; }
		public DateTime PurchaseTime { get; init; }
		public DerivOrderCondition Condition { get; init; }
		public bool IsClosed { get; set; }
		public bool IsCloseTradeSent { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, DerivActiveSymbol> _symbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, DerivSubscription> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, DerivOrderTracker> _orders = [];
	private readonly HashSet<long> _seenTransactions = [];
	private DerivRestClient _restClient;
	private DerivWebSocketClient _webSocketClient;
	private DerivRestAccount _account;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _lastPing;
	private DateTime _lastAccountRefresh;

	/// <summary>Initializes a new instance of the <see cref="DerivMessageAdapter"/> class.</summary>
	public DerivMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(DerivExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Level1 ||
			dataType == DataType.Ticks || dataType.IsTFCandles ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Deriv];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Deriv) ||
			securityId.IsAssociated(BoardCodes.Deriv);

	private DerivWebSocketClient WebSocketClient
		=> _webSocketClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private string PortfolioName
		=> _account?.AccountId ?? throw new InvalidOperationException(
			"Deriv authentication is required for this operation.");

	private void EnsureAuthenticated()
	{
		if (_account is null || _restClient is null)
			throw new InvalidOperationException(
				"Deriv access token and application ID are required for private operations.");
	}

	private DerivActiveSymbol ResolveSymbol(SecurityId securityId)
	{
		var symbol = (securityId.Native as string).IsEmpty(securityId.SecurityCode);
		if (symbol.IsEmpty())
			throw new ArgumentException("Deriv security code is required.", nameof(securityId));
		using (_sync.EnterScope())
		{
			return _symbols.TryGetValue(symbol, out var value)
				? value
				: throw new InvalidOperationException(
					$"Deriv symbol '{symbol}' was not found in active symbols.");
		}
	}

	private void AddSubscription(DerivSubscription subscription)
	{
		if (subscription is null)
			throw new ArgumentNullException(nameof(subscription));
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryAdd(subscription.NativeKey, subscription))
				throw new InvalidOperationException(
					$"Deriv subscription '{subscription.NativeKey}' is already tracked.");
		}
	}

	private bool TryGetSubscription(string nativeKey, out DerivSubscription subscription)
	{
		using (_sync.EnterScope())
			return _subscriptions.TryGetValue(nativeKey, out subscription);
	}

	private bool TryRemoveSubscription(string nativeKey, out DerivSubscription subscription)
	{
		using (_sync.EnterScope())
		{
			if (!_subscriptions.TryGetValue(nativeKey, out subscription))
				return false;
			_subscriptions.Remove(nativeKey);
			return true;
		}
	}

	private DerivSubscription[] GetSubscriptions(long transactionId,
		params DerivSubscriptionKinds[] kinds)
	{
		using (_sync.EnterScope())
			return [.. _subscriptions.Values.Where(item =>
				item.TransactionId == transactionId && kinds.Contains(item.Kind))];
	}

	private void TrackOrder(DerivOrderTracker order)
	{
		using (_sync.EnterScope())
			_orders[order.ContractId] = order;
	}

	private DerivOrderTracker GetOrder(long contractId)
	{
		using (_sync.EnterScope())
			return _orders.TryGetValue(contractId, out var order) ? order : null;
	}

	private long ResolveContractId(long? orderId, long originalTransactionId)
	{
		if (orderId is > 0)
			return orderId.Value;
		using (_sync.EnterScope())
			return _orders.Values.FirstOrDefault(order =>
				order.TransactionId == originalTransactionId)?.ContractId ?? 0;
	}
}
