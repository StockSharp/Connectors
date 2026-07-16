namespace StockSharp.Questrade;

public partial class QuestradeMessageAdapter
{
	private sealed class QuoteSubscription
	{
		public long TransactionId { get; init; }
		public long SymbolId { get; init; }
		public SecurityId SecurityId { get; init; }
	}

	private QuestradeRestClient _client;
	private QuestradeAccount[] _accounts = [];
	private QuestradeAccount _selectedAccount;
	private CancellationTokenSource _lifetimeCts;
	private CancellationTokenSource _quoteCts;
	private Task _notificationTask;
	private Task _sessionTask;
	private Task _quoteTask;
	private readonly SemaphoreSlim _quoteGate = new(1, 1);
	private readonly CachedSynchronizedDictionary<long, QuoteSubscription> _quoteSubscriptions = [];
	private readonly SynchronizedDictionary<long, QuestradeSymbol> _symbols = [];
	private readonly SynchronizedDictionary<long, long> _orderTransactions = [];
	private readonly SynchronizedSet<long> _executions = [];
	private long _orderStatusSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="QuestradeMessageAdapter"/> class.</summary>
	public QuestradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(QuestradeExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["QUESTRADE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		_client = new(AccessToken?.UnSecure(), RefreshToken?.UnSecure(), ApiServer) { Parent = this };
		_client.CredentialsChanged += OnCredentialsChanged;
		try
		{
			await _client.Connect(cancellationToken);
			_accounts = (await _client.GetAccounts(cancellationToken)).Accounts ?? [];
			_selectedAccount = ResolveConfiguredAccount();
			_lifetimeCts = new CancellationTokenSource();
			_notificationTask = RunNotifications(_lifetimeCts.Token);
			_sessionTask = RunSessionMonitor(_lifetimeCts.Token);
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
		CancelStreams();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClient();
		_accounts = [];
		_selectedAccount = null;
		_quoteSubscriptions.Clear();
		_symbols.Clear();
		_orderTransactions.Clear();
		_executions.Clear();
		_orderStatusSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void OnCredentialsChanged(string accessToken, string refreshToken, string apiServer)
	{
		AccessToken = accessToken.Secure();
		RefreshToken = refreshToken.Secure();
		ApiServer = apiServer;
	}

	private QuestradeAccount ResolveConfiguredAccount()
	{
		if (!Account.IsEmpty())
			return _accounts.FirstOrDefault(a => a.Number.EqualsIgnoreCase(Account))
				?? throw new InvalidOperationException($"Questrade account '{Account}' was not found.");
		return _accounts.FirstOrDefault(a => a.IsPrimary) ?? _accounts.FirstOrDefault()
			?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private QuestradeAccount ResolveAccount(string account)
	{
		if (account.IsEmpty())
			return _selectedAccount ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
		return _accounts.FirstOrDefault(a => a.Number.EqualsIgnoreCase(account))
			?? throw new InvalidOperationException($"Questrade account '{account}' was not found.");
	}

	private async Task RunNotifications(CancellationToken cancellationToken)
	{
		var failures = 0;
		while (!cancellationToken.IsCancellationRequested)
		{
			var started = DateTime.UtcNow;
			try
			{
				var port = (await _client.GetNotificationPort(cancellationToken)).StreamPort;
				if (port <= 0)
					throw new InvalidOperationException("Questrade notification endpoint returned an invalid stream port.");
				var socket = new QuestradeWebSocketClient(_client.ApiServer, port, () => _client.AccessToken) { Parent = this };
				await socket.Run<QuestradeNotification>(ProcessNotification, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
			}
			if (!cancellationToken.IsCancellationRequested)
			{
				failures = DateTime.UtcNow - started > TimeSpan.FromMinutes(1) ? 0 : Math.Min(failures + 1, 4);
				await Task.Delay(TimeSpan.FromSeconds(1 << failures), cancellationToken);
			}
		}
	}

	private async Task RunSessionMonitor(CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			try
			{
				await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
				await _client.GetTime(cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, CancellationToken.None);
			}
		}
	}

	private async ValueTask ProcessNotification(QuestradeNotification notification, CancellationToken cancellationToken)
	{
		if (notification == null)
			return;
		var account = notification.AccountNumber.IsEmpty(_selectedAccount?.Number);
		foreach (var order in notification.Orders ?? [])
			await ProcessOrder(order, account, 0, cancellationToken);
		foreach (var execution in notification.Executions ?? [])
			await ProcessExecution(execution, account, 0, cancellationToken);
	}

	private void CancelStreams()
	{
		_quoteCts?.Cancel();
		_quoteCts?.Dispose();
		_quoteCts = null;
		_lifetimeCts?.Cancel();
		_lifetimeCts?.Dispose();
		_lifetimeCts = null;
		_notificationTask = null;
		_sessionTask = null;
		_quoteTask = null;
	}

	private void DisposeClient()
	{
		CancelStreams();
		if (_client == null)
			return;
		_client.CredentialsChanged -= OnCredentialsChanged;
		_client.Dispose();
		_client = null;
	}
}
