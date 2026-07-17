namespace StockSharp.CapitalFutures.Native;

sealed class CapitalFuturesSdkClient : BaseLogReceiver
{
	private sealed class CallbackWorkItem
	{
		public CallbackWorkItem(Func<CancellationToken, ValueTask> callback,
			bool isErrorReportingEnabled)
		{
			Callback = callback;
			IsErrorReportingEnabled = isErrorReportingEnabled;
		}

		public Func<CancellationToken, ValueTask> Callback { get; }
		public bool IsErrorReportingEnabled { get; }
	}

	private readonly string _sdkPath;
	private readonly string _login;
	private readonly SecureString _password;
	private readonly string _requestedAccount;
	private readonly CapitalFuturesEnvironments _environment;
	private readonly bool _isTradingEnabled;
	private readonly string _logPath;
	private readonly SemaphoreSlim _subscriptionSync = new(1, 1);
	private readonly ConcurrentDictionary<long, CapitalSubscription> _subscriptions = [];
	private CapitalFuturesSdkBridge _bridge;
	private CancellationTokenSource _lifetime;
	private Channel<CallbackWorkItem> _callbackChannel;
	private Task _callbackTask;
	private TaskCompletionSource<CapitalAccountInfo> _accountSelected;
	private int _isDisposed;

	public CapitalFuturesSdkClient(string sdkPath, string login, SecureString password,
		string requestedAccount, CapitalFuturesEnvironments environment,
		bool isTradingEnabled, string logPath)
	{
		_sdkPath = sdkPath;
		_login = login;
		_password = password;
		_requestedAccount = requestedAccount;
		_environment = environment;
		_isTradingEnabled = isTradingEnabled;
		_logPath = logPath;
	}

	public override string Name => nameof(CapitalFutures) + "_" + nameof(CapitalFuturesSdkClient);

	public event Func<CapitalSubscription, CapitalInstrumentInfo, CancellationToken, ValueTask> QuoteReceived;
	public event Func<CapitalSubscription, CapitalTradeUpdate, CancellationToken, ValueTask> TradeReceived;
	public event Func<CapitalSubscription, CapitalBookUpdate, CancellationToken, ValueTask> BookReceived;
	public event Func<CapitalOrderReport, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public string Version => _bridge?.Version;
	public CapitalAccountInfo Account { get; private set; }
	public bool IsTradingEnabled => _isTradingEnabled;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_bridge != null)
			throw new InvalidOperationException("Capital Futures SDK is already connected.");
		_lifetime = new();
		_callbackChannel = Channel.CreateUnbounded<CallbackWorkItem>(new()
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
		});
		_callbackTask = ProcessCallbacksAsync(_callbackChannel.Reader, _lifetime.Token);
		_accountSelected = _isTradingEnabled
			? new(TaskCreationOptions.RunContinuationsAsynchronously)
			: null;

		try
		{
			var bridge = new CapitalFuturesSdkBridge(_sdkPath, _login, _password,
				_environment, _isTradingEnabled, _logPath);
			bridge.QuoteReceived += OnQuote;
			bridge.TradeReceived += OnTrade;
			bridge.BookReceived += OnBook;
			bridge.OrderReceived += OnOrder;
			bridge.Error += OnError;
			bridge.ConnectionLost += OnConnectionLost;
			_bridge = bridge;

			await bridge.ConnectAsync(cancellationToken);
			if (_isTradingEnabled)
			{
				var accounts = bridge.Accounts.Where(item => item.IsDomesticFutures).ToArray();
				Account = _requestedAccount.IsEmpty()
					? accounts.FirstOrDefault()
					: accounts.FirstOrDefault(item =>
						item.FullAccount.EqualsIgnoreCase(_requestedAccount) ||
						item.Account.EqualsIgnoreCase(_requestedAccount));
				if (Account == null)
					throw new InvalidOperationException(_requestedAccount.IsEmpty()
						? "Capital API returned no domestic futures account (TF)."
						: $"Capital API did not return requested domestic futures account '{_requestedAccount}'.");
				_accountSelected.TrySetResult(Account);
				await FlushCallbacksAsync(cancellationToken);
			}
			this.AddInfoLog("Capital Futures API {0} connected for {1}.",
				bridge.Version, Account?.FullAccount.IsEmpty(_login));
		}
		catch
		{
			await DisconnectAsync();
			throw;
		}
	}

	public async Task DisconnectAsync()
	{
		var lifetime = _lifetime;
		_lifetime = null;
		var accountSelected = _accountSelected;
		_accountSelected = null;
		lifetime?.Cancel();
		accountSelected?.TrySetCanceled();
		var callbackChannel = _callbackChannel;
		_callbackChannel = null;
		callbackChannel?.Writer.TryComplete();
		var callbackTask = _callbackTask;
		_callbackTask = null;

		var bridge = _bridge;
		_bridge = null;
		if (bridge != null)
		{
			bridge.QuoteReceived -= OnQuote;
			bridge.TradeReceived -= OnTrade;
			bridge.BookReceived -= OnBook;
			bridge.OrderReceived -= OnOrder;
			bridge.Error -= OnError;
			bridge.ConnectionLost -= OnConnectionLost;
			try
			{
				await bridge.DisconnectAsync();
			}
			finally
			{
				bridge.Dispose();
			}
		}
		if (callbackTask != null)
		{
			try
			{
				await callbackTask;
			}
			catch (OperationCanceledException)
			{
			}
		}
		lifetime?.Dispose();
		Account = null;
		_subscriptions.Clear();
	}

	public Task<CapitalInstrumentInfo> GetInstrumentAsync(string symbol,
		SecurityTypes? securityType, CancellationToken cancellationToken)
		=> EnsureBridge().GetInstrumentAsync(symbol, securityType, cancellationToken);

	public async Task SubscribeAsync(CapitalSubscription subscription,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		await _subscriptionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryAdd(subscription.TransactionId, subscription))
				return;
			if (_subscriptions.Values.Count(item => HasSameNativeFeed(item, subscription)) > 1)
				return;
			try
			{
				if (subscription.Kind == CapitalMarketDataKinds.Level1)
					await EnsureBridge().SubscribeLevel1Async(subscription.Symbol, cancellationToken);
				else
					await EnsureBridge().SubscribeTicksAsync(subscription.Symbol, cancellationToken);
			}
			catch
			{
				_subscriptions.TryRemove(subscription.TransactionId, out _);
				throw;
			}
		}
		finally
		{
			_subscriptionSync.Release();
		}
	}

	public async Task UnsubscribeAsync(long transactionId, CancellationToken cancellationToken)
	{
		await _subscriptionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryRemove(transactionId, out var subscription))
				return;
			if (_subscriptions.Values.Any(item => HasSameNativeFeed(item, subscription)))
				return;
			if (subscription.Kind == CapitalMarketDataKinds.Level1)
				await EnsureBridge().UnsubscribeLevel1Async(subscription.Symbol, cancellationToken);
			else
				await EnsureBridge().UnsubscribeTicksAsync(subscription.Symbol, cancellationToken);
		}
		finally
		{
			_subscriptionSync.Release();
		}
	}

	public Task<CapitalOrderResponse> PlaceOrderAsync(CapitalOrderRequest request,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().PlaceOrderAsync(request, cancellationToken);
	}

	public Task<CapitalOrderResponse> CancelOrderAsync(string account, string orderId,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().CancelOrderAsync(account, orderId, cancellationToken);
	}

	public Task<CapitalOrderResponse> ReplacePriceAsync(string account, string orderId,
		decimal price, TimeInForce timeInForce, CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().ReplacePriceAsync(account, orderId, price, timeInForce, cancellationToken);
	}

	public Task<CapitalOrderResponse> DecreaseOrderAsync(string account, string orderId,
		int decreaseVolume, CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().DecreaseOrderAsync(account, orderId, decreaseVolume, cancellationToken);
	}

	public Task<CapitalPortfolioSnapshot> GetPortfolioAsync(CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().GetPortfolioAsync(Account.FullAccount, cancellationToken);
	}

	public Task<bool> IsConnectedAsync(CancellationToken cancellationToken)
		=> EnsureBridge().IsConnectedAsync(cancellationToken);

	public Task KeepAliveAsync(CancellationToken cancellationToken)
		=> EnsureBridge().KeepAliveAsync(cancellationToken);

	protected override void DisposeManaged()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			try
			{
				DisconnectAsync().GetAwaiter().GetResult();
			}
			catch
			{
			}
			_subscriptionSync.Dispose();
		}
		base.DisposeManaged();
	}

	private void OnQuote(CapitalInstrumentInfo instrument)
		=> QueueCallback(async cancellationToken =>
		{
			if (QuoteReceived is not { } handler)
				return;
			foreach (var subscription in MatchSubscriptions(CapitalMarketDataKinds.Level1,
				instrument.SecurityType, instrument.Symbol))
				await handler(subscription, instrument, cancellationToken);
		});

	private void OnTrade(CapitalTradeUpdate update)
		=> QueueCallback(async cancellationToken =>
		{
			if (TradeReceived is not { } handler)
				return;
			var securityType = update.MarketNo.ToSecurityType();
			foreach (var subscription in MatchSubscriptions(CapitalMarketDataKinds.Trades,
				securityType, update.Symbol))
				await handler(subscription, update, cancellationToken);
		});

	private void OnBook(CapitalBookUpdate update)
		=> QueueCallback(async cancellationToken =>
		{
			if (BookReceived is not { } handler)
				return;
			var securityType = update.MarketNo.ToSecurityType();
			foreach (var subscription in MatchSubscriptions(CapitalMarketDataKinds.MarketDepth,
				securityType, update.Symbol))
				await handler(subscription, update, cancellationToken);
		});

	private void OnOrder(CapitalOrderReport report)
		=> QueueCallback(async cancellationToken =>
		{
			var selection = _accountSelected;
			var account = selection == null
				? Account
				: await selection.Task.WaitAsync(cancellationToken);
			if (account == null || !report.Account.EqualsIgnoreCase(account.FullAccount) ||
				OrderReceived is not { } handler)
				return;
			await handler(report, cancellationToken);
		});

	private void OnError(Exception error)
		=> QueueCallback(cancellationToken => Error is { } handler
			? handler(error, cancellationToken)
			: default, false);

	private void OnConnectionLost(Exception error)
		=> QueueCallback(cancellationToken => ConnectionLost is { } handler
			? handler(error, cancellationToken)
			: default);

	private void QueueCallback(Func<CancellationToken, ValueTask> callback, bool reportErrors = true)
		=> _callbackChannel?.Writer.TryWrite(new(callback, reportErrors));

	private async Task FlushCallbacksAsync(CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		if (_callbackChannel?.Writer.TryWrite(new(_ =>
		{
			completion.TrySetResult(null);
			return default;
		}, false)) != true)
			throw new InvalidOperationException("Capital Futures callback channel is not available.");
		await completion.Task.WaitAsync(cancellationToken);
	}

	private async Task ProcessCallbacksAsync(ChannelReader<CallbackWorkItem> reader,
		CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var item in reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					await item.Callback(cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
				}
				catch (Exception error)
				{
					if (item.IsErrorReportingEnabled && Error is { } handler)
					{
						try
						{
							await handler(error, CancellationToken.None);
						}
						catch (Exception handlerError)
						{
							this.AddErrorLog(handlerError);
						}
					}
					else
						this.AddErrorLog(error);
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
	}

	private CapitalSubscription[] MatchSubscriptions(CapitalMarketDataKinds kind,
		SecurityTypes securityType, string symbol)
		=> [.. _subscriptions.Values.Where(item => item.Kind == kind &&
			item.SecurityType == securityType && item.Symbol.EqualsIgnoreCase(symbol))];

	private static bool HasSameNativeFeed(CapitalSubscription left, CapitalSubscription right)
		=> left.Symbol.EqualsIgnoreCase(right.Symbol) &&
			(left.Kind == CapitalMarketDataKinds.Level1) == (right.Kind == CapitalMarketDataKinds.Level1);

	private CapitalFuturesSdkBridge EnsureBridge()
		=> _bridge ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureTrading()
	{
		if (!_isTradingEnabled)
			throw new InvalidOperationException("Capital Futures trading services are disabled by configuration.");
		if (Account == null)
			throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}
}
