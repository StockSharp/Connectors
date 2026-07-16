namespace StockSharp.Groww.Native;

internal sealed class GrowwFeedClient : BaseLogReceiver
{
	private sealed class Subscription : IDisposable
	{
		public CancellationTokenSource Cancellation { get; init; }
		public Task Task { get; set; }

		public void Dispose() => Cancellation.Dispose();
	}

	private const string _socketUrl = "wss://socket-api.groww.in";
	private readonly GrowwRestClient _rest;
	private readonly int _reconnectAttempts;
	private readonly SynchronizedDictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
	private CancellationTokenSource _lifetime;
	private NatsConnection _connection;
	private KeyPair _keyPair;
	private bool _wasConnected;

	public GrowwFeedClient(GrowwRestClient rest, int reconnectAttempts)
	{
		_rest = rest ?? throw new ArgumentNullException(nameof(rest));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
	}

	public override string Name => nameof(Groww) + "_NATS";

	public string SubscriptionId { get; private set; }

	public event Func<string, byte[], CancellationToken, ValueTask> DataReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask Connect(CancellationToken cancellationToken)
	{
		if (_connection != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_keyPair = KeyPair.CreatePair(PrefixByte.User);
		var credentials = await _rest.CreateSocketToken(_keyPair.GetPublicKey(), cancellationToken);
		SubscriptionId = credentials.SubscriptionId.ThrowIfEmpty(nameof(credentials.SubscriptionId));
		_lifetime = new CancellationTokenSource();

		var options = NatsOpts.Default with
		{
			Url = _socketUrl,
			Name = "StockSharp Groww",
			Headers = false,
			PingInterval = TimeSpan.FromSeconds(60),
			MaxReconnectRetry = _reconnectAttempts,
			RetryOnInitialConnect = true,
			AuthOpts = NatsAuthOpts.Default with
			{
				Jwt = credentials.Token.ThrowIfEmpty(nameof(credentials.Token)),
				Seed = _keyPair.GetSeed(),
			},
		};

		_connection = new(options);
		_connection.ConnectionOpened += OnConnectionOpened;
		_connection.ConnectionDisconnected += OnConnectionDisconnected;
		_connection.ReconnectFailed += OnReconnectFailed;
		_connection.MessageDropped += OnMessageDropped;
		try
		{
			await _connection.ConnectAsync().AsTask().WaitAsync(cancellationToken);
		}
		catch
		{
			await Disconnect();
			throw;
		}
	}

	public ValueTask Subscribe(string subject, CancellationToken cancellationToken)
	{
		subject.ThrowIfEmpty(nameof(subject));
		if (_connection == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		if (_subscriptions.ContainsKey(subject))
			return default;

		cancellationToken.ThrowIfCancellationRequested();
		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
		var subscription = new Subscription { Cancellation = cancellation };
		if (!_subscriptions.TryAdd(subject, subscription))
		{
			subscription.Dispose();
			return default;
		}

		subscription.Task = Consume(subject, cancellation.Token);
		return default;
	}

	public async ValueTask Unsubscribe(string subject)
	{
		if (!_subscriptions.Remove(subject, out var subscription))
			return;
		subscription.Cancellation.Cancel();
		await AwaitSubscription(subscription.Task);
		subscription.Dispose();
	}

	public async ValueTask Disconnect()
	{
		_lifetime?.Cancel();
		foreach (var subscription in _subscriptions.Values.ToArray())
			await AwaitSubscription(subscription.Task);
		foreach (var subscription in _subscriptions.Values.ToArray())
			subscription.Dispose();
		_subscriptions.Clear();

		if (_connection != null)
		{
			_connection.ConnectionOpened -= OnConnectionOpened;
			_connection.ConnectionDisconnected -= OnConnectionDisconnected;
			_connection.ReconnectFailed -= OnReconnectFailed;
			_connection.MessageDropped -= OnMessageDropped;
			await _connection.DisposeAsync();
			_connection = null;
		}

		_lifetime?.Dispose();
		_lifetime = null;
		_keyPair?.Dispose();
		_keyPair = null;
		SubscriptionId = null;
		_wasConnected = false;
	}

	protected override void DisposeManaged()
	{
		Disconnect().AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}

	private async Task Consume(string subject, CancellationToken cancellationToken)
	{
		try
		{
			await foreach (var message in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: cancellationToken))
			{
				if (message.Data is not { Length: > 0 } data || DataReceived is not { } handler)
					continue;
				await handler(subject, data, cancellationToken);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			if (Error is { } handler)
				await handler(ex, CancellationToken.None);
		}
	}

	private async ValueTask OnConnectionOpened(object sender, NatsEventArgs args)
	{
		var state = _wasConnected ? ConnectionStates.Restored : ConnectionStates.Connected;
		_wasConnected = true;
		if (StateChanged is { } handler)
			await handler(state, _lifetime?.Token ?? CancellationToken.None);
	}

	private async ValueTask OnConnectionDisconnected(object sender, NatsEventArgs args)
	{
		if (StateChanged is { } handler)
			await handler(ConnectionStates.Disconnected, _lifetime?.Token ?? CancellationToken.None);
	}

	private async ValueTask OnReconnectFailed(object sender, NatsEventArgs args)
	{
		if (StateChanged is { } stateHandler)
			await stateHandler(ConnectionStates.Failed, _lifetime?.Token ?? CancellationToken.None);
		if (Error is { } errorHandler)
			await errorHandler(new InvalidOperationException($"Groww NATS reconnect failed: {args.Message}"), CancellationToken.None);
	}

	private async ValueTask OnMessageDropped(object sender, NatsMessageDroppedEventArgs args)
	{
		if (Error is { } handler)
			await handler(new InvalidOperationException($"Groww NATS dropped a message for '{args.Subject}' with {args.Pending} pending messages."), CancellationToken.None);
	}

	private static async ValueTask AwaitSubscription(Task task)
	{
		if (task == null)
			return;
		try
		{
			await task;
		}
		catch (OperationCanceledException)
		{
		}
	}
}
