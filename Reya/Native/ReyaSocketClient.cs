namespace StockSharp.Reya.Native;

sealed class ReyaSocketClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _subscriptions =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, TaskCompletionSource<ReyaSocketHeader>>
		_pending = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;
	private long _requestId;

	public ReyaSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Reya_WS";

	public event Func<ReyaSocketEnvelope<ReyaMarketSummary>, CancellationToken,
		ValueTask> PerpetualSummaryReceived;
	public event Func<ReyaSocketEnvelope<ReyaSpotMarketSummary>, CancellationToken,
		ValueTask> SpotSummaryReceived;
	public event Func<ReyaSocketEnvelope<ReyaPrice>, CancellationToken, ValueTask>
		PriceReceived;
	public event Func<ReyaSocketEnvelope<ReyaDepth>, CancellationToken, ValueTask>
		DepthReceived;
	public event Func<ReyaSocketEnvelope<ReyaPerpetualExecution[]>,
		CancellationToken, ValueTask> PerpetualExecutionsReceived;
	public event Func<ReyaSocketEnvelope<ReyaSpotExecution[]>, CancellationToken,
		ValueTask> SpotExecutionsReceived;
	public event Func<ReyaSocketEnvelope<ReyaPosition[]>, CancellationToken,
		ValueTask> PositionsReceived;
	public event Func<ReyaSocketEnvelope<ReyaOrder[]>, CancellationToken,
		ValueTask> OrdersReceived;
	public event Func<ReyaSocketEnvelope<ReyaAccountBalance[]>, CancellationToken,
		ValueTask> BalancesReceived;
	public event Func<long, CancellationToken, ValueTask> ServerTimeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException(
				"Reya WebSocket is already initialized.");
		var client = _client = CreateClient();
		try
		{
			await client.ConnectAsync(cancellationToken);
			await PingAsync(cancellationToken);
		}
		catch
		{
			await DisposeClientAsync(cancellationToken);
			throw;
		}
	}

	public ValueTask DisconnectAsync(CancellationToken cancellationToken)
		=> DisposeClientAsync(cancellationToken);

	public ValueTask SubscribeAsync(string channel,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, true, cancellationToken);

	public ValueTask UnsubscribeAsync(string channel,
		CancellationToken cancellationToken)
		=> ChangeSubscriptionAsync(channel, false, cancellationToken);

	public async ValueTask PingAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		if (client?.IsConnected != true)
			return;
		await SendAsync(client, new ReyaSocketPingCommand
		{
			Type = ReyaSocketMessageTypes.Ping,
			Id = NextId(),
			Timestamp = DateTime.UtcNow.ToReyaMilliseconds(),
		}, cancellationToken);
	}

	private WebSocketClient CreateClient()
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			_endpoint,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			static (s, a) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket =>
		{
			socket.Options.SetRequestHeader("User-Agent",
				"StockSharp-Reya-Connector/1.0");
			socket.Options.DangerousDeflateOptions = new();
		};
		return client;
	}

	private async ValueTask ChangeSubscriptionAsync(string channel,
		bool isSubscribe, CancellationToken cancellationToken)
	{
		channel = NormalizeChannel(channel);
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe
				? _subscriptions.Add(channel)
				: _subscriptions.Remove(channel);
		if (!changed || _client?.IsConnected != true)
			return;
		try
		{
			await SendSubscriptionAsync(_client, channel, isSubscribe, true,
				cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				if (isSubscribe)
					_subscriptions.Remove(channel);
				else
					_subscriptions.Add(channel);
			throw;
		}
	}

	private async ValueTask SendSubscriptionAsync(WebSocketClient client,
		string channel, bool isSubscribe, bool isWaitResponse,
		CancellationToken cancellationToken)
	{
		var expectedType = isSubscribe
			? ReyaSocketMessageTypes.Subscribed
			: ReyaSocketMessageTypes.Unsubscribed;
		var pendingKey = PendingKey(expectedType, channel);
		TaskCompletionSource<ReyaSocketHeader> pending = null;
		if (isWaitResponse)
			pending = CreatePending(pendingKey);
		try
		{
			await SendAsync(client, new ReyaSocketSubscriptionCommand
			{
				Type = isSubscribe
					? ReyaSocketMessageTypes.Subscribe
					: ReyaSocketMessageTypes.Unsubscribe,
				Channel = channel,
				Id = NextId(),
				IsBatched = false,
			}, cancellationToken);
			if (pending is not null)
				await pending.Task.WaitAsync(TimeSpan.FromSeconds(10),
					cancellationToken);
		}
		finally
		{
			if (pending is not null)
				RemovePending(pendingKey);
		}
	}

	private string NextId()
		=> Interlocked.Increment(ref _requestId).ToString(
			CultureInfo.InvariantCulture);

	private TaskCompletionSource<ReyaSocketHeader> CreatePending(string key)
	{
		var pending = new TaskCompletionSource<ReyaSocketHeader>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using (_sync.EnterScope())
			_pending.Add(key, pending);
		return pending;
	}

	private void RemovePending(string key)
	{
		using (_sync.EnterScope())
			_pending.Remove(key);
	}

	private async ValueTask SendAsync<TPayload>(WebSocketClient client,
		TPayload payload, CancellationToken cancellationToken)
	{
		await _sendSync.WaitAsync(cancellationToken);
		try
		{
			await client.SendAsync(payload, cancellationToken);
		}
		finally
		{
			_sendSync.Release();
		}
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<ReyaSocketHeader>(payload);
			if (header.Timestamp is long timestamp && timestamp > 0)
				await RaiseAsync(ServerTimeReceived, timestamp, cancellationToken);
			switch (header.Type)
			{
				case ReyaSocketMessageTypes.Ping:
					await SendAsync(client, new ReyaSocketPongCommand
					{
						Type = ReyaSocketMessageTypes.Pong,
						Id = header.Id,
						Timestamp = header.Timestamp,
					}, cancellationToken);
					return;
				case ReyaSocketMessageTypes.Pong:
					return;
				case ReyaSocketMessageTypes.Subscribed:
					CompletePending(header);
					await ProcessSubscribedSnapshotAsync(payload, header.Channel,
						cancellationToken);
					return;
				case ReyaSocketMessageTypes.Unsubscribed:
					CompletePending(header);
					return;
				case ReyaSocketMessageTypes.Error:
					var error = new InvalidOperationException(
						"Reya WebSocket error" +
						(header.Channel.IsEmpty()
							? string.Empty
							: " on '" + header.Channel + "'") + ": " +
						(header.Message.IsEmpty()
							? "unknown error"
							: header.Message));
					FailPending(header.Channel, error);
					await RaiseErrorAsync(error, cancellationToken);
					return;
				case ReyaSocketMessageTypes.ChannelData:
					await ProcessChannelDataAsync(payload, header.Channel,
						cancellationToken);
					return;
				default:
					throw new InvalidDataException(
						"Unsupported Reya WebSocket message type '" +
						header.Type + "'.");
			}
		}
		catch (Exception error) when (!cancellationToken.IsCancellationRequested)
		{
			await RaiseErrorAsync(new InvalidDataException(
				"Failed to process a Reya WebSocket message.", error),
				cancellationToken);
		}
	}

	private async ValueTask ProcessSubscribedSnapshotAsync(string payload,
		string channel, CancellationToken cancellationToken)
	{
		channel = NormalizeChannel(channel);
		if (!channel.StartsWith("/v2/market/", StringComparison.Ordinal) ||
			!channel.EndsWith("/depth", StringComparison.Ordinal))
			return;
		ReyaSocketSubscribedEnvelope<ReyaDepth> snapshot;
		try
		{
			snapshot = Deserialize<ReyaSocketSubscribedEnvelope<ReyaDepth>>(
				payload);
		}
		catch (InvalidDataException error)
		{
			this.AddWarningLog(
				"Reya depth subscription contains no usable snapshot: {0}",
				error.Message);
			return;
		}
		if (snapshot.Contents is null)
			return;
		await RaiseAsync(DepthReceived, new ReyaSocketEnvelope<ReyaDepth>
		{
			Type = ReyaSocketMessageTypes.ChannelData,
			Channel = snapshot.Channel,
			Id = snapshot.Id,
			Timestamp = snapshot.Timestamp,
			Data = snapshot.Contents,
		}, cancellationToken);
	}

	private async ValueTask ProcessChannelDataAsync(string payload,
		string channel, CancellationToken cancellationToken)
	{
		channel = NormalizeChannel(channel);
		if (channel.StartsWith("/v2/spotMarket/", StringComparison.Ordinal) &&
			channel.EndsWith("/summary", StringComparison.Ordinal))
			await RaiseAsync(SpotSummaryReceived, Deserialize<
				ReyaSocketEnvelope<ReyaSpotMarketSummary>>(payload),
				cancellationToken);
		else if (channel.StartsWith("/v2/market/", StringComparison.Ordinal) &&
			channel.EndsWith("/summary", StringComparison.Ordinal))
			await RaiseAsync(PerpetualSummaryReceived, Deserialize<
				ReyaSocketEnvelope<ReyaMarketSummary>>(payload), cancellationToken);
		else if (channel.StartsWith("/v2/prices/", StringComparison.Ordinal))
			await RaiseAsync(PriceReceived, Deserialize<
				ReyaSocketEnvelope<ReyaPrice>>(payload), cancellationToken);
		else if (channel.StartsWith("/v2/market/", StringComparison.Ordinal) &&
			channel.EndsWith("/depth", StringComparison.Ordinal))
			await RaiseAsync(DepthReceived, Deserialize<
				ReyaSocketEnvelope<ReyaDepth>>(payload), cancellationToken);
		else if (channel.EndsWith("/perpExecutions", StringComparison.Ordinal))
			await RaiseAsync(PerpetualExecutionsReceived, Deserialize<
				ReyaSocketEnvelope<ReyaPerpetualExecution[]>>(payload),
				cancellationToken);
		else if (channel.EndsWith("/spotExecutions", StringComparison.Ordinal))
			await RaiseAsync(SpotExecutionsReceived, Deserialize<
				ReyaSocketEnvelope<ReyaSpotExecution[]>>(payload),
				cancellationToken);
		else if (channel.EndsWith("/positions", StringComparison.Ordinal) &&
			channel.StartsWith("/v2/wallet/", StringComparison.Ordinal))
			await RaiseAsync(PositionsReceived, Deserialize<
				ReyaSocketEnvelope<ReyaPosition[]>>(payload), cancellationToken);
		else if (channel.EndsWith("/orderChanges", StringComparison.Ordinal) &&
			channel.StartsWith("/v2/wallet/", StringComparison.Ordinal))
			await RaiseAsync(OrdersReceived, Deserialize<
				ReyaSocketEnvelope<ReyaOrder[]>>(payload), cancellationToken);
		else if (channel.EndsWith("/accountBalances", StringComparison.Ordinal) &&
			channel.StartsWith("/v2/wallet/", StringComparison.Ordinal))
			await RaiseAsync(BalancesReceived, Deserialize<
				ReyaSocketEnvelope<ReyaAccountBalance[]>>(payload),
				cancellationToken);
		else
			this.AddWarningLog("Unknown Reya WebSocket channel '{0}'.", channel);
	}

	private void CompletePending(ReyaSocketHeader response)
	{
		if (response.Channel.IsEmpty())
			throw new InvalidDataException(
				"Reya WebSocket acknowledgement has no channel.");
		TaskCompletionSource<ReyaSocketHeader> pending;
		using (_sync.EnterScope())
			_pending.TryGetValue(PendingKey(response.Type, response.Channel),
				out pending);
		pending?.TrySetResult(response);
	}

	private void FailPending(string channel, Exception error)
	{
		TaskCompletionSource<ReyaSocketHeader>[] pending;
		using (_sync.EnterScope())
		{
			if (channel.IsEmpty())
			{
				pending = [.. _pending.Values];
				_pending.Clear();
			}
			else
			{
				var suffix = "|" + NormalizeChannel(channel);
				var keys = _pending.Keys.Where(key => key.EndsWith(suffix,
					StringComparison.Ordinal)).ToArray();
				pending = keys.Select(key => _pending[key]).ToArray();
				foreach (var key in keys)
					_pending.Remove(key);
			}
		}
		foreach (var completion in pending)
			completion.TrySetException(error);
	}

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
		{
			string[] subscriptions;
			using (_sync.EnterScope())
				subscriptions = [.. _subscriptions];
			foreach (var channel in subscriptions)
				await SendSubscriptionAsync(client, channel, true, false,
					cancellationToken);
		}
		else if (state == ConnectionStates.Failed)
			FailPending(null, new InvalidOperationException(
				"Reya WebSocket connection failed."));
		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask DisposeClientAsync(
		CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		FailPending(null, new InvalidOperationException(
			"Reya WebSocket was disconnected."));
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

	private T Deserialize<T>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(payload, _jsonSettings) ??
				throw new InvalidDataException(
					"Reya returned an empty WebSocket JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Reya returned malformed WebSocket JSON.", error);
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

	private static string NormalizeChannel(string channel)
	{
		channel = channel.ThrowIfEmpty(nameof(channel)).Trim();
		return channel.StartsWith('/') ? channel : "/" + channel;
	}

	private static string PendingKey(ReyaSocketMessageTypes type,
		string channel)
		=> type + "|" + NormalizeChannel(channel);

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_client = null;
		_sendSync.Dispose();
		base.DisposeManaged();
	}
}
