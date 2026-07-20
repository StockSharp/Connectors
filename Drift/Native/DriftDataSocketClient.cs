namespace StockSharp.Drift.Native;

sealed class DriftDataSocketClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, DriftDataSubscribeRequest>
		_subscriptions = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public DriftDataSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.NormalizeSocketEndpoint(nameof(endpoint));
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Drift_Data_WS";

	public event Func<DriftMarket[], CancellationToken, ValueTask>
		MarketsReceived;
	public event Func<DriftCandle, CancellationToken, ValueTask> CandleReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Drift data WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeMarketsAsync(CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("markets", new()
		{
			ChannelType = DriftDataChannels.Markets,
		}, true, cancellationToken);

	public ValueTask UnsubscribeMarketsAsync(
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync("markets", new()
		{
			ChannelType = DriftDataChannels.Markets,
		}, false, cancellationToken);

	public ValueTask SubscribeCandleAsync(string symbol, string resolution,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(CandleKey(symbol, resolution), new()
		{
			ChannelType = DriftDataChannels.Candle,
			Symbol = symbol,
			Resolution = resolution,
		}, true, cancellationToken);

	public ValueTask UnsubscribeCandleAsync(string symbol, string resolution,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(CandleKey(symbol, resolution), new()
		{
			ChannelType = DriftDataChannels.Candle,
			Symbol = symbol,
			Resolution = resolution,
		}, false, cancellationToken);

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(_endpoint.ToString(),
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a), static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		client.Init += socket => socket.Options.DangerousDeflateOptions = new();
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(string key,
		DriftDataSubscribeRequest request, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		bool changed;
		using (_sync.EnterScope())
		{
			if (isSubscribe)
				changed = _subscriptions.TryAdd(key, request);
			else
				changed = _subscriptions.Remove(key);
		}
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendAsync(_client, new DriftDataSubscribeRequest
			{
				Type = isSubscribe ? "subscribe" : "unsubscribe",
				ChannelType = request.ChannelType,
				Symbol = request.Symbol,
				Resolution = request.Resolution,
			}, cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (isSubscribe)
					_subscriptions.Remove(key);
				else
					_subscriptions[key] = request;
			}
			throw;
		}
	}

	private async ValueTask SendAsync<T>(WebSocketClient client, T request,
		CancellationToken cancellationToken)
	{
		await _sendGate.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(request, cancellationToken);
		}
		finally
		{
			_sendGate.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<DriftDataSocketHeader>(payload);
			if (header.Type.EqualsIgnoreCase("error"))
				throw new DriftApiException(
					"Drift data WebSocket error: " +
					(header.Message.IsEmpty() ? "unknown error" : header.Message));
			if (header.ChannelType.EqualsIgnoreCase("markets") &&
				header.Type is "init" or "update")
			{
				var update = Deserialize<DriftMarketsSocketMessage>(payload);
				await RaiseAsync(MarketsReceived, update.Data ?? [],
					cancellationToken);
			}
			else if (header.ChannelType.EqualsIgnoreCase("candle") ||
				header.Type is "create" && payload.Contains("\"candle\"",
					StringComparison.Ordinal))
			{
				var update = Deserialize<DriftCandleSocketMessage>(payload);
				if (update.Candle is not null)
					await RaiseAsync(CandleReceived, update.Candle,
						cancellationToken);
			}
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Drift data WebSocket message.", error),
				cancellationToken);
		}
	}

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _settings) ??
				throw new InvalidDataException(
					"Drift returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Drift returned malformed WebSocket JSON.", error);
		}
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			DriftDataSubscribeRequest[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions.Values];
			foreach (var request in subscriptions)
				await SendAsync(client, request, cancellationToken);
		}
		await RaiseAsync(StateChanged, state, cancellationToken);
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		try
		{
			if (client.IsConnected)
				await client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			client.Dispose();
		}
	}

	private async ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
	{
		if (Error is { } handler)
			await handler(error, cancellationToken);
	}

	private static ValueTask RaiseAsync<T>(
		Func<T, CancellationToken, ValueTask> handler, T value,
		CancellationToken cancellationToken)
		=> handler is null ? default : handler(value, cancellationToken);

	private static string CandleKey(string symbol, string resolution)
		=> symbol.ThrowIfEmpty(nameof(symbol)).Trim().ToUpperInvariant() +
			"|" + resolution.ThrowIfEmpty(nameof(resolution)).Trim();

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendGate.Dispose();
		base.DisposeManaged();
	}
}
