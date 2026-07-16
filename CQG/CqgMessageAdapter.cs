namespace StockSharp.CQG;

public partial class CqgMessageAdapter
{
	private sealed class ContractRecord
	{
		public ContractMetadata Metadata { get; init; }
		public SecurityId SecurityId { get; init; }
	}

	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public SecurityId SecurityId { get; init; }
		public DataType DataType { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public bool IsHistoryOnly { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public long? Count { get; init; }
		public uint RequestId { get; set; }
		public uint HistoryRequestId { get; set; }
		public uint ContractId { get; set; }
	}

	private sealed class OrderRecord
	{
		public long TransactionId { get; set; }
		public string OrderId { get; set; }
		public string ClientOrderId { get; set; }
		public int AccountId { get; set; }
		public CqgOrder Details { get; set; }
	}

	private CqgWebSocketClient _client;
	private int _nextRequestId;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _subscriptions = [];
	private readonly SynchronizedDictionary<uint, long> _requestTransactions = [];
	private readonly SynchronizedDictionary<uint, TaskCompletionSource<InformationReport>> _informationRequests = [];
	private readonly SynchronizedDictionary<string, ContractRecord> _contractsBySymbol = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<uint, ContractRecord> _contractsById = [];
	private readonly SynchronizedDictionary<string, OrderRecord> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _clientOrderTransactions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<int, string> _accountNames = [];
	private readonly SynchronizedDictionary<uint, long> _tradeSubscriptionTransactions = [];
	private readonly SynchronizedSet<uint> _historyOnlyTradeSubscriptions = [];
	private readonly SynchronizedDictionary<uint, HashSet<uint>> _tradeCompletionScopes = [];
	private readonly SynchronizedDictionary<uint, long> _historicalOrderRequests = [];
	private readonly SynchronizedSet<uint> _historyOnlyInformationRequests = [];
	private readonly SynchronizedSet<string> _fills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<uint, SortedDictionary<decimal, QuoteChange>> _bids = [];
	private readonly SynchronizedDictionary<uint, SortedDictionary<decimal, QuoteChange>> _asks = [];
	private readonly SynchronizedDictionary<string, OpenPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _positionSnapshots = new(StringComparer.OrdinalIgnoreCase);
	private uint _tradeSubscriptionId;
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private TaskCompletionSource _tradeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>Initializes a new instance of the <see cref="CqgMessageAdapter"/> class.</summary>
	public CqgMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(CqgExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["CQG", "CME", "CBOT", "COMEX", "NYMEX", "ICE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		var token = AccessToken?.UnSecure();
		if (token.IsEmpty() && (UserName.IsEmpty() || Password?.UnSecure().IsEmpty() != false))
			throw new InvalidOperationException("CQG username and password or an access token are required.");
		var logon = new Logon
		{
			ClientVersion = ClientVersion.ThrowIfEmpty(nameof(ClientVersion)),
			ProtocolVersionMajor = 2,
			ProtocolVersionMinor = 296,
			MaxCollapsingLevel = (uint)CollapsingLevel,
			DropConcurrentSession = true,
			DialectId = "0",
		};
		if (!token.IsEmpty())
			logon.AccessToken = token;
		else
		{
			logon.UserName = UserName;
			logon.Password = Password.UnSecure();
			logon.OneTimePassword = OneTimePassword?.UnSecure() ?? string.Empty;
			logon.PrivateLabel = PrivateLabel.ThrowIfEmpty(nameof(PrivateLabel));
			logon.ClientAppId = ClientId.ThrowIfEmpty(nameof(ClientId));
		}
		_client = new(Endpoint, logon, ReConnectionSettings.ReAttemptCount) { Parent = this };
		_client.MessageReceived += ProcessServerMessage;
		_client.Connected += RestoreSession;
		_client.Error += error => SendOutErrorAsync(error, CancellationToken.None);
		try
		{
			await _client.Connect(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await _client.Disconnect();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClient();
		_subscriptions.Clear();
		_requestTransactions.Clear();
		_informationRequests.Clear();
		_contractsBySymbol.Clear();
		_contractsById.Clear();
		_orders.Clear();
		_clientOrderTransactions.Clear();
		_accountNames.Clear();
		_fills.Clear();
		_bids.Clear();
		_asks.Clear();
		_positions.Clear();
		_positionSnapshots.Clear();
		_tradeSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_tradeSubscriptionTransactions.Clear();
		_historyOnlyTradeSubscriptions.Clear();
		_tradeCompletionScopes.Clear();
		_historicalOrderRequests.Clear();
		_historyOnlyInformationRequests.Clear();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask RestoreSession(CancellationToken cancellationToken)
	{
		_contractsBySymbol.Clear();
		_contractsById.Clear();
		_bids.Clear();
		_asks.Clear();
		_positions.Clear();
		_positionSnapshots.Clear();
		_tradeSubscriptionTransactions.Clear();
		_historyOnlyTradeSubscriptions.Clear();
		_tradeCompletionScopes.Clear();
		_tradeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_tradeSubscriptionId = NextRequestId();
		var summaryParameters = new AccountSummaryParameters();
		summaryParameters.RequestedFields.Add([4, 6, 8, 9, 15, 16, 17]);
		var trade = new TradeSubscription
		{
			Id = _tradeSubscriptionId,
			Subscribe = true,
			PublicationType = 4,
			AccountSummaryParameters = summaryParameters,
		};
		trade.SubscriptionScopes.Add([1, 2, 4]);
		var tradeMessage = new ClientMsg();
		tradeMessage.TradeSubscriptions.Add(trade);
		await _client.Send(tradeMessage, cancellationToken);
		if (_orderStatusSubscriptionId != 0)
			await RestoreOrderSubscription(_orderStatusSubscriptionId, cancellationToken);
		if (_portfolioSubscriptionId != 0)
			await RestorePortfolioSubscription(_portfolioSubscriptionId, cancellationToken);

		foreach (var symbol in _subscriptions.CachedValues.Select(s => s.Symbol).Distinct(StringComparer.OrdinalIgnoreCase))
			await SendSymbolResolution(symbol, null, cancellationToken);
	}

	private async ValueTask ProcessServerMessage(ServerMsg message, CancellationToken cancellationToken)
	{
		foreach (var userMessage in message.UserMessages)
			await SendOutErrorAsync(new InvalidOperationException($"CQG: {userMessage.Subject}: {userMessage.Text}"), cancellationToken);
		foreach (var report in message.InformationReports)
			await ProcessInformationReport(report, cancellationToken);
		foreach (var status in message.MarketDataSubscriptionStatuses)
			await ProcessMarketStatus(status, cancellationToken);
		foreach (var data in message.RealTimeMarketData)
			await ProcessMarketData(data, cancellationToken);
		foreach (var report in message.TimeAndSalesReports)
			await ProcessTimeAndSales(report, cancellationToken);
		foreach (var report in message.TimeBarReports)
			await ProcessTimeBars(report, cancellationToken);
		foreach (var order in message.OrderStatuses)
			foreach (var metadata in order.ContractMetadata)
				CacheContract(metadata);
		foreach (var position in message.PositionStatuses.Where(p => p.ContractMetadata != null))
			CacheContract(position.ContractMetadata);
		foreach (var reject in message.OrderRequestRejects)
			await ProcessOrderReject(reject, cancellationToken);
		foreach (var ack in message.OrderRequestAcks)
			_requestTransactions.Remove(ack.RequestId);
		foreach (var status in message.TradeSubscriptionStatuses)
		{
			if (status.Id == _tradeSubscriptionId)
			{
				if (status.StatusCode == 0)
					_tradeReady.TrySetResult();
				else
					_tradeReady.TrySetException(new InvalidOperationException(
						$"CQG trade subscription failed ({status.StatusCode}): {status.TextMessage}"));
			}
			if (status.StatusCode >= 100)
				await SendOutErrorAsync(new InvalidOperationException(
					$"CQG trade subscription {status.Id} failed ({status.StatusCode}): {status.TextMessage}"), cancellationToken);
		}
		foreach (var order in message.OrderStatuses)
			await ProcessOrderStatus(order, cancellationToken);
		foreach (var position in message.PositionStatuses)
			await ProcessPosition(position, cancellationToken);
		foreach (var summary in message.AccountSummaryStatuses)
			await ProcessAccountSummary(summary, cancellationToken);
		foreach (var completion in message.TradeSnapshotCompletions)
			await ProcessTradeSnapshotCompletion(completion, cancellationToken);
	}

	private uint NextRequestId()
	{
		var value = Interlocked.Increment(ref _nextRequestId);
		if (value <= 0)
		{
			Interlocked.Exchange(ref _nextRequestId, 1);
			value = 1;
		}
		return (uint)value;
	}

	private DateTime ToServerTime(long relativeMilliseconds)
		=> _client.BaseTime.AddMilliseconds(relativeMilliseconds);

	private void DisposeClient()
	{
		if (_client == null)
			return;
		_client.MessageReceived -= ProcessServerMessage;
		_client.Connected -= RestoreSession;
		_client.Dispose();
		_client = null;
	}

	private async ValueTask RestoreOrderSubscription(long transactionId, CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		_tradeSubscriptionTransactions[id] = transactionId;
		var subscription = new TradeSubscription { Id = id, Subscribe = true, PublicationType = 4 };
		subscription.SubscriptionScopes.Add(1);
		var message = new ClientMsg();
		message.TradeSubscriptions.Add(subscription);
		await _client.Send(message, cancellationToken);
	}

	private async ValueTask RestorePortfolioSubscription(long transactionId, CancellationToken cancellationToken)
	{
		var id = NextRequestId();
		_tradeSubscriptionTransactions[id] = transactionId;
		var summaryParameters = new AccountSummaryParameters();
		summaryParameters.RequestedFields.Add([4, 6, 8, 9, 15, 16, 17]);
		var subscription = new TradeSubscription
		{
			Id = id,
			Subscribe = true,
			PublicationType = 4,
			AccountSummaryParameters = summaryParameters,
		};
		subscription.SubscriptionScopes.Add([2, 4]);
		var message = new ClientMsg();
		message.TradeSubscriptions.Add(subscription);
		await _client.Send(message, cancellationToken);
	}
}
