namespace StockSharp.Intrinio.Native;

sealed class IntrinioRealtimeClient : BaseLogReceiver, IDisposable
{
	private readonly string _apiKey;
	private readonly IntrinioEquityProviders _equityProvider;
	private readonly IntrinioOptionProviders _optionProvider;
	private readonly bool _isDelayedOptions;
	private readonly int _equityThreads;
	private readonly int _optionThreads;
	private readonly int _equityBufferSize;
	private readonly int _optionBufferSize;
	private readonly SemaphoreSlim _sync = new(1, 1);
	private readonly IntrinioSubscriptionIndex _subscriptions = new();

	private IntrinioRealtimeConnection _equities;
	private IntrinioRealtimeConnection _options;
	private bool _isStopped;
	private int _isDisposed;

	public IntrinioRealtimeClient(string apiKey,
		IntrinioEquityProviders equityProvider,
		IntrinioOptionProviders optionProvider,
		bool isDelayedOptions,
		int equityThreads,
		int optionThreads,
		int equityBufferSize,
		int optionBufferSize)
	{
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
		_equityProvider = equityProvider;
		_optionProvider = optionProvider;
		_isDelayedOptions = isDelayedOptions;
		_equityThreads = equityThreads;
		_optionThreads = optionThreads;
		_equityBufferSize = equityBufferSize;
		_optionBufferSize = optionBufferSize;
	}

	public event Func<IntrinioStreamSubscription, IntrinioDecodedEvent,
		CancellationToken, ValueTask> EventReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async Task SubscribeAsync(IntrinioStreamSubscription subscription,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(subscription);
		await _sync.WaitAsync(cancellationToken);
		try
		{
			if (_isStopped)
				throw new ObjectDisposedException(nameof(IntrinioRealtimeClient));
			if (!_subscriptions.TryAdd(subscription, out var first))
			{
				throw new InvalidOperationException(
					$"Intrinio subscription {subscription.TransactionId} already exists.");
			}

			try
			{
				if (!first)
					return;

				var connection = subscription.IsOption
					? await EnsureOptionsAsync(cancellationToken)
					: await EnsureEquitiesAsync(cancellationToken);
				await connection.JoinAsync(subscription.Symbol, cancellationToken);
			}
			catch
			{
				if (_subscriptions.TryRemove(subscription.TransactionId,
					out var removed, out _))
				{
					await removed.DeactivateAsync();
				}
				throw;
			}
		}
		finally
		{
			_sync.Release();
		}
	}

	public async Task<IntrinioUnsubscription?> UnsubscribeAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		IntrinioStreamSubscription subscription;
		bool last;
		ValueTask deactivation;
		await _sync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryGet(transactionId, out subscription, out last))
				return null;

			if (last)
			{
				var connection = subscription.IsOption ? _options : _equities;
				if (connection != null)
					await connection.LeaveAsync(subscription.Symbol, cancellationToken);
			}

			if (!_subscriptions.TryRemove(transactionId, out var removed,
				out var removedLast) ||
				!ReferenceEquals(subscription, removed) ||
				last != removedLast)
			{
				throw new InvalidOperationException(
					$"Intrinio subscription index changed while removing {transactionId}.");
			}

			deactivation = subscription.DeactivateAsync();
		}
		finally
		{
			_sync.Release();
		}

		await deactivation;
		return new(subscription.Symbol, subscription.IsOption, last);
	}

	public async Task StopAsync()
	{
		IntrinioRealtimeConnection equities;
		IntrinioRealtimeConnection options;
		IntrinioStreamSubscription[] subscriptions;
		Task[] deactivations;
		await _sync.WaitAsync();
		try
		{
			if (_isStopped)
				return;
			_isStopped = true;
			subscriptions = _subscriptions.Clear();
			deactivations = [.. subscriptions.Select(subscription =>
				subscription.DeactivateAsync().AsTask())];

			equities = _equities;
			_equities = null;
			options = _options;
			_options = null;
		}
		finally
		{
			_sync.Release();
		}

		var errors = new List<Exception>();

		foreach (var connection in new[] { equities, options })
		{
			if (connection == null)
				continue;
			try
			{
				await connection.StopAsync();
			}
			catch (Exception error)
			{
				errors.Add(error);
			}
			finally
			{
				connection.Dispose();
			}
		}

		await Task.WhenAll(deactivations);
		if (errors.Count > 0)
			throw new AggregateException("Failed to stop Intrinio WebSocket clients.", errors);
	}

	private async Task<IntrinioRealtimeConnection> EnsureEquitiesAsync(
		CancellationToken cancellationToken)
	{
		if (_equities != null)
			return _equities;

		var connection = new IntrinioRealtimeConnection(_apiKey, _equityProvider,
			_equityThreads, _equityBufferSize) { Parent = this };
		connection.EventReceived += OnDecodedEvent;
		connection.Error += OnConnectionError;
		try
		{
			await connection.StartAsync(cancellationToken);
			_equities = connection;
			return connection;
		}
		catch
		{
			connection.EventReceived -= OnDecodedEvent;
			connection.Error -= OnConnectionError;
			connection.Dispose();
			throw;
		}
	}

	private async Task<IntrinioRealtimeConnection> EnsureOptionsAsync(
		CancellationToken cancellationToken)
	{
		if (_options != null)
			return _options;

		var connection = new IntrinioRealtimeConnection(_apiKey, _optionProvider,
			_isDelayedOptions, _optionThreads, _optionBufferSize) { Parent = this };
		connection.EventReceived += OnDecodedEvent;
		connection.Error += OnConnectionError;
		try
		{
			await connection.StartAsync(cancellationToken);
			_options = connection;
			return connection;
		}
		catch
		{
			connection.EventReceived -= OnDecodedEvent;
			connection.Error -= OnConnectionError;
			connection.Dispose();
			throw;
		}
	}

	private async ValueTask OnDecodedEvent(IntrinioDecodedEvent update,
		CancellationToken cancellationToken)
	{
		if (EventReceived is not { } handler)
			return;

		foreach (var subscription in MatchSubscriptions(update))
		{
			if (!subscription.TryEnterDelivery())
				continue;

			try
			{
				await handler(subscription, update, cancellationToken);
			}
			finally
			{
				subscription.ExitDelivery();
			}
		}
	}

	private ValueTask OnConnectionError(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;

	private IntrinioStreamSubscription[] MatchSubscriptions(IntrinioDecodedEvent update)
		=> _subscriptions.Match(update.IsOption, update.Symbol);

	protected override void DisposeManaged()
	{
		if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
		{
			try
			{
				StopAsync().GetAwaiter().GetResult();
			}
			catch
			{
			}
			_sync.Dispose();
		}
		base.DisposeManaged();
	}
}
