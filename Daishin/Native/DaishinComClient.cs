namespace StockSharp.Daishin.Native;

[SupportedOSPlatform("windows")]
sealed class DaishinComClient : BaseLogReceiver
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

	private readonly string _account;
	private readonly DaishinStockMarkets _market;
	private readonly bool _isTradingEnabled;
	private readonly SemaphoreSlim _subscriptionSync = new(1, 1);
	private readonly ConcurrentDictionary<long, DaishinSubscription> _subscriptions = [];
	private DaishinComBridge _bridge;
	private CancellationTokenSource _lifetime;
	private Channel<CallbackWorkItem> _callbackChannel;
	private Task _callbackTask;
	private int _isDisposed;

	public DaishinComClient(string account, DaishinStockMarkets market, bool isTradingEnabled)
	{
		_account = account;
		_market = market;
		_isTradingEnabled = isTradingEnabled;
	}

	public override string Name => nameof(Daishin) + "_" + nameof(DaishinComClient);

	public event Func<DaishinSubscription, DaishinLevel1Update, CancellationToken, ValueTask> Level1Received;
	public event Func<DaishinSubscription, DaishinBookUpdate, CancellationToken, ValueTask> BookReceived;
	public event Func<DaishinOrderUpdate, CancellationToken, ValueTask> OrderReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<Exception, CancellationToken, ValueTask> ConnectionLost;

	public string Version => _bridge?.Version;
	public IReadOnlyList<DaishinAccountInfo> Accounts => _bridge?.Accounts ?? [];
	public bool IsTradingEnabled => _isTradingEnabled;

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		if (_bridge != null)
			throw new InvalidOperationException("Daishin CYBOS Plus is already connected.");

		_lifetime = new();
		_callbackChannel = Channel.CreateUnbounded<CallbackWorkItem>(new()
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
		});
		_callbackTask = ProcessCallbacksAsync(_callbackChannel.Reader, _lifetime.Token);

		try
		{
			var bridge = new DaishinComBridge(_account, _market, _isTradingEnabled);
			bridge.Level1Received += OnLevel1;
			bridge.BookReceived += OnBook;
			bridge.OrderReceived += OnOrder;
			bridge.Error += OnError;
			bridge.ConnectionLost += OnConnectionLost;
			_bridge = bridge;
			await bridge.ConnectAsync(cancellationToken);
			this.AddInfoLog("Daishin {0} connected{1}.", bridge.Version,
				_isTradingEnabled ? $" for {string.Join(", ", bridge.Accounts.Select(item => item.Account))}" : null);
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
		lifetime?.Cancel();
		var channel = _callbackChannel;
		_callbackChannel = null;
		channel?.Writer.TryComplete();
		var callbackTask = _callbackTask;
		_callbackTask = null;

		var bridge = _bridge;
		_bridge = null;
		if (bridge != null)
		{
			bridge.Level1Received -= OnLevel1;
			bridge.BookReceived -= OnBook;
			bridge.OrderReceived -= OnOrder;
			bridge.Error -= OnError;
			bridge.ConnectionLost -= OnConnectionLost;
			try
			{
				await bridge.DisconnectAsync(CancellationToken.None);
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
		_subscriptions.Clear();
	}

	public Task<bool> IsConnectedAsync(CancellationToken cancellationToken)
		=> EnsureBridge().IsConnectedAsync(cancellationToken);

	public Task<IReadOnlyList<DaishinSecurityInfo>> GetSecuritiesAsync(string code,
		ISet<SecurityTypes> securityTypes, CancellationToken cancellationToken)
		=> EnsureBridge().GetSecuritiesAsync(code, securityTypes, cancellationToken);

	public Task<DaishinLevel1Update> GetSnapshotAsync(DaishinSecurityInfo security,
		CancellationToken cancellationToken)
		=> EnsureBridge().GetSnapshotAsync(security, cancellationToken);

	public Task<IReadOnlyList<DaishinCandle>> GetCandlesAsync(DaishinSecurityInfo security,
		TimeSpan timeFrame, int count, CancellationToken cancellationToken)
		=> EnsureBridge().GetCandlesAsync(security, timeFrame, count, cancellationToken);

	public async Task SubscribeAsync(DaishinSubscription subscription,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		await _subscriptionSync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryAdd(subscription.TransactionId, subscription))
				return;
			if (_subscriptions.Values.Count(item =>
				item.NativeKey.EqualsIgnoreCase(subscription.NativeKey)) > 1)
				return;
			try
			{
				await EnsureBridge().SubscribeAsync(subscription, cancellationToken);
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
			if (_subscriptions.Values.Any(item =>
				item.NativeKey.EqualsIgnoreCase(subscription.NativeKey)))
				return;
			await EnsureBridge().UnsubscribeAsync(subscription.NativeKey, cancellationToken);
		}
		finally
		{
			_subscriptionSync.Release();
		}
	}

	public Task<DaishinOrderResponse> PlaceOrderAsync(DaishinOrderRequest request,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().PlaceOrderAsync(request, cancellationToken);
	}

	public Task<DaishinOrderResponse> ReplaceOrderAsync(string orderId,
		DaishinOrderRequest request, CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().ReplaceOrderAsync(orderId, request, cancellationToken);
	}

	public Task<DaishinOrderResponse> CancelOrderAsync(string orderId,
		DaishinOrderRequest request, CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().CancelOrderAsync(orderId, request, cancellationToken);
	}

	public Task<IReadOnlyList<DaishinOrderUpdate>> GetOpenOrdersAsync(string account,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().GetOpenOrdersAsync(account, cancellationToken);
	}

	public Task<DaishinPortfolioSnapshot> GetPortfolioAsync(string account,
		CancellationToken cancellationToken)
	{
		EnsureTrading();
		return EnsureBridge().GetPortfolioAsync(account, cancellationToken);
	}

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

	private void OnLevel1(DaishinLevel1Update update)
		=> QueueCallback(async cancellationToken =>
		{
			if (Level1Received is not { } handler)
				return;
			foreach (var subscription in MatchSubscriptions(
				DaishinMarketDataKinds.Current, update.SecurityType, update.Code))
				await handler(subscription, update, cancellationToken);
		});

	private void OnBook(DaishinBookUpdate update)
		=> QueueCallback(async cancellationToken =>
		{
			if (BookReceived is not { } handler)
				return;
			foreach (var subscription in MatchSubscriptions(
				DaishinMarketDataKinds.MarketDepth, update.SecurityType, update.Code))
				await handler(subscription, update, cancellationToken);
		});

	private void OnOrder(DaishinOrderUpdate update)
		=> QueueCallback(cancellationToken => OrderReceived is { } handler
			? handler(update, cancellationToken)
			: default);

	private void OnError(Exception error)
		=> QueueCallback(cancellationToken => Error is { } handler
			? handler(error, cancellationToken)
			: default, false);

	private void OnConnectionLost(Exception error)
		=> QueueCallback(cancellationToken => ConnectionLost is { } handler
			? handler(error, cancellationToken)
			: default);

	private DaishinSubscription[] MatchSubscriptions(DaishinMarketDataKinds kind,
		SecurityTypes securityType, string code)
		=> [.. _subscriptions.Values.Where(item => item.Kind == kind &&
			item.SecurityType == securityType && item.Code.EqualsIgnoreCase(code))];

	private void QueueCallback(Func<CancellationToken, ValueTask> callback, bool reportErrors = true)
		=> _callbackChannel?.Writer.TryWrite(new(callback, reportErrors));

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

	private DaishinComBridge EnsureBridge()
		=> _bridge ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureTrading()
	{
		if (!_isTradingEnabled)
			throw new InvalidOperationException("Daishin CYBOS Plus trading services are disabled by configuration.");
		_ = EnsureBridge();
	}
}
