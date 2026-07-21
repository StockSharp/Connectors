namespace StockSharp.Kaiko.Native;

sealed class KaikoStreamClient : BaseLogReceiver
{
	private sealed class Session
	{
		public CancellationTokenSource Lifetime { get; init; }
		public Task Task { get; set; }
	}

	private readonly GrpcChannel _channel;
	private readonly StreamMarketUpdateServiceV1.
		StreamMarketUpdateServiceV1Client _marketClient;
	private readonly StreamAggregatesOHLCVServiceV1.
		StreamAggregatesOHLCVServiceV1Client _ohlcvClient;
	private readonly Metadata _headers;
	private readonly Func<StreamMarketUpdateResponseV1, CancellationToken,
		ValueTask> _marketHandler;
	private readonly Func<StreamAggregatesOHLCVResponseV1, CancellationToken,
		ValueTask> _ohlcvHandler;
	private readonly Func<Exception, CancellationToken, ValueTask> _errorHandler;
	private readonly int _maximumReconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<KaikoStreamKey, Session> _sessions = [];
	private bool _isDisposed;

	public KaikoStreamClient(string endpoint, SecureString apiKey,
		int maximumReconnectAttempts,
		Func<StreamMarketUpdateResponseV1, CancellationToken, ValueTask>
			marketHandler,
		Func<StreamAggregatesOHLCVResponseV1, CancellationToken, ValueTask>
			ohlcvHandler,
		Func<Exception, CancellationToken, ValueTask> errorHandler)
	{
		if (!Uri.TryCreate(endpoint?.Trim(), UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || uri.Host.IsEmpty() ||
			!uri.Query.IsEmpty() || !uri.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid HTTPS Kaiko Stream endpoint is required.",
				nameof(endpoint));
		var key = apiKey?.UnSecure()?.Trim();
		if (key.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		ArgumentOutOfRangeException.ThrowIfNegative(maximumReconnectAttempts);
		_marketHandler = marketHandler ?? throw new ArgumentNullException(
			nameof(marketHandler));
		_ohlcvHandler = ohlcvHandler ?? throw new ArgumentNullException(
			nameof(ohlcvHandler));
		_errorHandler = errorHandler ?? throw new ArgumentNullException(
			nameof(errorHandler));
		_maximumReconnectAttempts = maximumReconnectAttempts;
		_headers = new() { { "authorization", "Bearer " + key } };
		_channel = GrpcChannel.ForAddress(uri);
		var invoker = _channel.CreateCallInvoker();
		_marketClient = new(invoker);
		_ohlcvClient = new(invoker);
	}

	public override string Name => "Kaiko_Stream";

	public ValueTask SubscribeAsync(KaikoStreamKey key,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		var lifetime = new CancellationTokenSource();
		var session = new Session { Lifetime = lifetime };
		using (_sync.EnterScope())
		{
			if (_sessions.ContainsKey(key))
			{
				lifetime.Dispose();
				throw new InvalidOperationException(
					$"Kaiko stream '{key}' is already subscribed.");
			}
			_sessions.Add(key, session);
			session.Task = RunOwnedAsync(key, session);
		}
		return default;
	}

	public async ValueTask UnsubscribeAsync(KaikoStreamKey key,
		CancellationToken cancellationToken)
	{
		Session session;
		using (_sync.EnterScope())
			if (!_sessions.Remove(key, out session))
				return;
		var isCallback = cancellationToken == session.Lifetime.Token;
		session.Lifetime.Cancel();
		if (isCallback)
			return;
		try
		{
			await session.Task.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException) when (
			session.Lifetime.IsCancellationRequested)
		{
		}
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		Session[] sessions;
		using (_sync.EnterScope())
		{
			sessions = [.. _sessions.Values];
			_sessions.Clear();
		}
		foreach (var session in sessions)
			session.Lifetime.Cancel();
		foreach (var session in sessions)
		{
			try
			{
				await session.Task.WaitAsync(cancellationToken);
			}
			catch (OperationCanceledException) when (
				session.Lifetime.IsCancellationRequested)
			{
			}
		}
	}

	private async Task RunOwnedAsync(KaikoStreamKey key, Session session)
	{
		try
		{
			await RunAsync(key, session.Lifetime.Token);
		}
		finally
		{
			session.Lifetime.Dispose();
		}
	}

	private async Task RunAsync(KaikoStreamKey key,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; !cancellationToken.IsCancellationRequested;
			attempt++)
		{
			try
			{
				if (key.Kind == KaikoStreamKinds.Ohlcv)
					await RunOhlcvAsync(key, cancellationToken);
				else
					await RunMarketAsync(key, cancellationToken);
				throw new IOException("Kaiko stream completed unexpectedly.");
			}
			catch (OperationCanceledException) when (
				cancellationToken.IsCancellationRequested)
			{
				break;
			}
			catch (RpcException error) when (
				IsTransient(error.StatusCode) &&
				attempt < _maximumReconnectAttempts)
			{
				this.AddWarningLog(() => error.ToString());
				await DelayReconnectAsync(attempt, cancellationToken);
			}
			catch (IOException error) when (
				attempt < _maximumReconnectAttempts)
			{
				this.AddWarningLog(() => error.ToString());
				await DelayReconnectAsync(attempt, cancellationToken);
			}
			catch (Exception error)
			{
				await _errorHandler(error, cancellationToken);
				break;
			}
		}
	}

	private async ValueTask RunMarketAsync(KaikoStreamKey key,
		CancellationToken cancellationToken)
	{
		var request = new StreamMarketUpdateRequestV1
		{
			InstrumentCriteria = CreateCriteria(key.Security),
		};
		request.Commodities.Add(key.Kind switch
		{
			KaikoStreamKinds.Trades =>
				StreamMarketUpdateCommodity.SmucTrade,
			KaikoStreamKinds.TopOfBook =>
				StreamMarketUpdateCommodity.SmucTopOfBook,
			_ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
		});
		using var call = _marketClient.Subscribe(request, _headers,
			cancellationToken: cancellationToken);
		while (await call.ResponseStream.MoveNext(cancellationToken))
		{
			var response = call.ResponseStream.Current ?? throw new
				InvalidDataException("Kaiko market stream returned an empty response.");
			await _marketHandler(response, cancellationToken);
		}
	}

	private async ValueTask RunOhlcvAsync(KaikoStreamKey key,
		CancellationToken cancellationToken)
	{
		var request = new StreamAggregatesOHLCVRequestV1
		{
			InstrumentCriteria = CreateCriteria(key.Security),
			Aggregate = key.TimeFrame.ToAggregate(),
		};
		using var call = _ohlcvClient.Subscribe(request, _headers,
			cancellationToken: cancellationToken);
		while (await call.ResponseStream.MoveNext(cancellationToken))
		{
			var response = call.ResponseStream.Current ?? throw new
				InvalidDataException("Kaiko OHLCV stream returned an empty response.");
			await _ohlcvHandler(response, cancellationToken);
		}
	}

	private static InstrumentCriteria CreateCriteria(KaikoSecurityKey key)
		=> new()
		{
			Exchange = key.Exchange,
			InstrumentClass = key.InstrumentClass.ToWire(),
			Code = key.Code,
		};

	private static bool IsTransient(StatusCode statusCode)
		=> statusCode is StatusCode.Cancelled or StatusCode.Unknown or
			StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted or
			StatusCode.Aborted or StatusCode.Internal or StatusCode.Unavailable;

	private static ValueTask DelayReconnectAsync(int attempt,
		CancellationToken cancellationToken)
	{
		var seconds = Math.Min(30, 1 << Math.Min(attempt, 5));
		return new(Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken));
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		Session[] sessions;
		using (_sync.EnterScope())
		{
			sessions = [.. _sessions.Values];
			_sessions.Clear();
		}
		foreach (var session in sessions)
		{
			session.Lifetime.Cancel();
		}
		_channel.Dispose();
		base.DisposeManaged();
	}
}
