namespace StockSharp.Intrinio.Native;

enum IntrinioRealtimeEventTypes
{
	EquityTrade,
	EquityQuote,
	OptionTrade,
	OptionQuote,
	OptionRefresh,
}

sealed class IntrinioRealtimeEvent
{
	private IntrinioRealtimeEvent(IntrinioRealtimeEventTypes type)
	{
		Type = type;
	}

	public IntrinioRealtimeEventTypes Type { get; }
	public EquityTrade EquityTrade { get; private init; }
	public EquityQuote EquityQuote { get; private init; }
	public OptionTrade OptionTrade { get; private init; }
	public OptionQuote OptionQuote { get; private init; }
	public OptionRefresh OptionRefresh { get; private init; }

	public string Symbol => Type switch
	{
		IntrinioRealtimeEventTypes.EquityTrade => EquityTrade.Symbol,
		IntrinioRealtimeEventTypes.EquityQuote => EquityQuote.Symbol,
		IntrinioRealtimeEventTypes.OptionTrade => OptionTrade.Contract,
		IntrinioRealtimeEventTypes.OptionQuote => OptionQuote.Contract,
		IntrinioRealtimeEventTypes.OptionRefresh => OptionRefresh.Contract,
		_ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null),
	};

	public bool IsOption => Type is IntrinioRealtimeEventTypes.OptionTrade or
		IntrinioRealtimeEventTypes.OptionQuote or IntrinioRealtimeEventTypes.OptionRefresh;

	public static IntrinioRealtimeEvent From(EquityTrade value)
		=> new(IntrinioRealtimeEventTypes.EquityTrade) { EquityTrade = value };

	public static IntrinioRealtimeEvent From(EquityQuote value)
		=> new(IntrinioRealtimeEventTypes.EquityQuote) { EquityQuote = value };

	public static IntrinioRealtimeEvent From(OptionTrade value)
		=> new(IntrinioRealtimeEventTypes.OptionTrade) { OptionTrade = value };

	public static IntrinioRealtimeEvent From(OptionQuote value)
		=> new(IntrinioRealtimeEventTypes.OptionQuote) { OptionQuote = value };

	public static IntrinioRealtimeEvent From(OptionRefresh value)
		=> new(IntrinioRealtimeEventTypes.OptionRefresh) { OptionRefresh = value };
}

sealed class IntrinioStreamSubscription
{
	public long TransactionId { get; init; }
	public SecurityId SecurityId { get; init; }
	public string Symbol { get; init; }
	public DataType DataType { get; init; }
	public bool IsOption { get; init; }
}

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
	private readonly ConcurrentDictionary<long, IntrinioStreamSubscription> _subscriptions = [];
	private readonly Channel<IntrinioRealtimeEvent> _events =
		Channel.CreateUnbounded<IntrinioRealtimeEvent>(new()
		{
			SingleReader = true,
			SingleWriter = false,
			AllowSynchronousContinuations = false,
		});
	private CancellationTokenSource _lifetime;
	private Task _pumpTask;
	private EquitiesWebSocketClient _equities;
	private OptionsWebSocketClient _options;
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

	public event Func<IntrinioStreamSubscription, IntrinioRealtimeEvent,
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
			if (_subscriptions.ContainsKey(subscription.TransactionId))
				throw new InvalidOperationException(
					$"Intrinio subscription {subscription.TransactionId} already exists.");

			EnsureEventPump();
			var first = !_subscriptions.Values.Any(existing =>
				HasSameNativeFeed(existing, subscription));
			if (!_subscriptions.TryAdd(subscription.TransactionId, subscription))
				throw new InvalidOperationException(
					$"Intrinio subscription {subscription.TransactionId} already exists.");
			try
			{
				if (!first)
					return;
				if (subscription.IsOption)
				{
					var client = await EnsureOptionsAsync();
					await client.Join(subscription.Symbol, false);
				}
				else
				{
					var client = await EnsureEquitiesAsync();
					await client.Join(subscription.Symbol, false);
				}
			}
			catch
			{
				_subscriptions.TryRemove(subscription.TransactionId, out _);
				throw;
			}
		}
		finally
		{
			_sync.Release();
		}
	}

	public async Task UnsubscribeAsync(long transactionId,
		CancellationToken cancellationToken)
	{
		await _sync.WaitAsync(cancellationToken);
		try
		{
			if (!_subscriptions.TryRemove(transactionId, out var subscription))
				return;
			if (_subscriptions.Values.Any(existing =>
				HasSameNativeFeed(existing, subscription)))
			{
				return;
			}

			try
			{
				if (subscription.IsOption)
				{
					if (_options is { } options)
						await options.Leave(subscription.Symbol);
				}
				else
				{
					if (_equities is { } equities)
						await equities.Leave(subscription.Symbol);
				}
			}
			catch
			{
				_subscriptions.TryAdd(transactionId, subscription);
				throw;
			}
		}
		finally
		{
			_sync.Release();
		}
	}

	public async Task StopAsync()
	{
		EquitiesWebSocketClient equities;
		OptionsWebSocketClient options;
		Task pumpTask;
		CancellationTokenSource lifetime;
		await _sync.WaitAsync();
		try
		{
			if (_isStopped)
				return;
			_isStopped = true;
			_subscriptions.Clear();

			equities = _equities;
			_equities = null;
			options = _options;
			_options = null;
			pumpTask = _pumpTask;
			_pumpTask = null;
			lifetime = _lifetime;
			_lifetime = null;
		}
		finally
		{
			_sync.Release();
		}

		Exception stopError = null;
		if (equities != null)
		{
			try
			{
				await equities.Stop();
			}
			catch (Exception error)
			{
				stopError = error;
			}
		}
		if (options != null)
		{
			try
			{
				await options.Stop();
			}
			catch (Exception error)
			{
				stopError = stopError == null
					? error
					: new AggregateException(stopError, error);
			}
		}

		_events.Writer.TryComplete();
		lifetime?.Cancel();
		if (pumpTask != null)
		{
			try
			{
				await pumpTask;
			}
			catch (OperationCanceledException)
			{
			}
		}
		lifetime?.Dispose();
		if (stopError != null)
			throw new AggregateException("Failed to stop Intrinio WebSocket clients.", stopError);
	}

	private void EnsureEventPump()
	{
		if (_pumpTask != null)
			return;
		_lifetime = new();
		_pumpTask = ProcessEventsAsync(_lifetime.Token);
	}

	private async Task<EquitiesWebSocketClient> EnsureEquitiesAsync()
	{
		if (_equities != null)
			return _equities;

		var config = new EquityConfig
		{
			ApiKey = _apiKey,
			Provider = _equityProvider.ToSdk(),
			Symbols = [],
			TradesOnly = false,
			NumThreads = _equityThreads,
			BufferSize = _equityBufferSize,
			Delayed = _equityProvider == IntrinioEquityProviders.DelayedSip,
		};
		var client = new EquitiesWebSocketClient(
			OnEquityTrade, OnEquityQuote, config);
		try
		{
			await client.Start();
			_equities = client;
			return client;
		}
		catch
		{
			try
			{
				await client.Stop();
			}
			catch
			{
			}
			throw;
		}
	}

	private async Task<OptionsWebSocketClient> EnsureOptionsAsync()
	{
		if (_options != null)
			return _options;

		var config = new OptionConfig
		{
			ApiKey = _apiKey,
			Provider = _optionProvider.ToSdk(),
			Symbols = [],
			TradesOnly = false,
			NumThreads = _optionThreads,
			BufferSize = _optionBufferSize,
			Delayed = _isDelayedOptions,
		};
		var client = new OptionsWebSocketClient(
			OnOptionTrade, OnOptionQuote, OnOptionRefresh, null, config);
		try
		{
			await client.Start();
			_options = client;
			return client;
		}
		catch
		{
			try
			{
				await client.Stop();
			}
			catch
			{
			}
			throw;
		}
	}

	private void OnEquityTrade(EquityTrade trade)
		=> _events.Writer.TryWrite(IntrinioRealtimeEvent.From(trade));

	private void OnEquityQuote(EquityQuote quote)
		=> _events.Writer.TryWrite(IntrinioRealtimeEvent.From(quote));

	private void OnOptionTrade(OptionTrade trade)
		=> _events.Writer.TryWrite(IntrinioRealtimeEvent.From(trade));

	private void OnOptionQuote(OptionQuote quote)
		=> _events.Writer.TryWrite(IntrinioRealtimeEvent.From(quote));

	private void OnOptionRefresh(OptionRefresh refresh)
		=> _events.Writer.TryWrite(IntrinioRealtimeEvent.From(refresh));

	private async Task ProcessEventsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var update in _events.Reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					if (EventReceived is not { } handler)
						continue;
					foreach (var subscription in MatchSubscriptions(update))
						await handler(subscription, update, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
				}
				catch (Exception error)
				{
					if (Error is { } errorHandler)
					{
						try
						{
							await errorHandler(error, cancellationToken);
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

	private IntrinioStreamSubscription[] MatchSubscriptions(IntrinioRealtimeEvent update)
		=> [.. _subscriptions.Values.Where(subscription =>
			subscription.IsOption == update.IsOption &&
			subscription.Symbol.EqualsIgnoreCase(update.Symbol))];

	private static bool HasSameNativeFeed(IntrinioStreamSubscription left,
		IntrinioStreamSubscription right)
		=> left.IsOption == right.IsOption && left.Symbol.EqualsIgnoreCase(right.Symbol);

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
