namespace StockSharp.Deepcoin.Native;

sealed class DeepcoinPrivateWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _listenKey;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<DeepcoinPrivateTables> _tables = [];
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public DeepcoinPrivateWsClient(string endpoint, string listenKey, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_listenKey = listenKey.ThrowIfEmpty(nameof(listenKey));
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Deepcoin) + "_PrivateWs";

	public event Func<DeepcoinPrivateAsset, CancellationToken, ValueTask> AssetReceived;
	public event Func<DeepcoinPrivateOrder, CancellationToken, ValueTask> OrderReceived;
	public event Func<DeepcoinPrivatePosition, CancellationToken, ValueTask> PositionReceived;
	public event Func<DeepcoinPrivateTrade, CancellationToken, ValueTask> TradeReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("Deepcoin private WebSocket is already initialized.");
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

	public ValueTask SubscribeTableAsync(DeepcoinPrivateTables table,
		CancellationToken cancellationToken)
		=> ChangeTableAsync(table, true, cancellationToken);

	public ValueTask UnsubscribeTableAsync(DeepcoinPrivateTables table,
		CancellationToken cancellationToken)
		=> ChangeTableAsync(table, false, cancellationToken);

	public ValueTask PingAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, "ping", cancellationToken)
			: default;

	private WebSocketClient CreateClient()
	{
		var separator = _endpoint.Contains('?', StringComparison.Ordinal) ? '&' : '?';
		var address = _endpoint + separator + "listenKey=" + Uri.EscapeDataString(_listenKey);
		WebSocketClient client = null;
		client = new WebSocketClient(
			address,
			(state, token) => OnStateChangedAsync(client, state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(socket, message, token) => OnProcessAsync(socket, message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _jsonSettings,
		};
		client.Init += socket => socket.Options.SetRequestHeader("User-Agent",
			"StockSharp-Deepcoin-Connector/2.0");
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client, ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
			await SendTablesAsync(client, cancellationToken);

		if (StateChanged is { } handler)
			await handler(state, cancellationToken);
	}

	private async ValueTask ChangeTableAsync(DeepcoinPrivateTables table, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe ? _tables.Add(table) : _tables.Remove(table);
		if (changed && _client?.IsConnected == true)
			await SendTablesAsync(_client, cancellationToken);
	}

	private ValueTask SendTablesAsync(WebSocketClient client,
		CancellationToken cancellationToken)
	{
		DeepcoinPrivateTables[] tables;
		using (_sync.EnterScope())
			tables = [.. _tables.OrderBy(static item => item)];
		return SendAsync(client, new DeepcoinPrivateCommand
		{
			Action = DeepcoinPrivateActions.Subscribe,
			Tables = tables,
		}, cancellationToken);
	}

	private async ValueTask SendAsync(WebSocketClient client, string payload,
		CancellationToken cancellationToken)
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

	private async ValueTask SendAsync<TPayload>(WebSocketClient client, TPayload payload,
		CancellationToken cancellationToken)
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

	private async ValueTask OnProcessAsync(WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty() || payload.EqualsIgnoreCase("pong"))
			return;

		try
		{
			var header = Deserialize<DeepcoinPrivateHeader>(payload);
			if (!header.Code.IsEmpty() && header.Code != "0")
				throw new InvalidOperationException(
					$"Deepcoin private WebSocket error {header.Code}: {header.Message}".Trim());

			switch (header.Action)
			{
				case DeepcoinPrivateActions.Subscribe:
					break;
				case DeepcoinPrivateActions.PushAccount:
					await DispatchAsync(Deserialize<DeepcoinPrivateEnvelope<DeepcoinPrivateAsset>>(payload).Result,
						AssetReceived, cancellationToken);
					break;
				case DeepcoinPrivateActions.PushOrder:
					await DispatchAsync(Deserialize<DeepcoinPrivateEnvelope<DeepcoinPrivateOrder>>(payload).Result,
						OrderReceived, cancellationToken);
					break;
				case DeepcoinPrivateActions.PushPosition:
					await DispatchAsync(Deserialize<DeepcoinPrivateEnvelope<DeepcoinPrivatePosition>>(payload).Result,
						PositionReceived, cancellationToken);
					break;
				case DeepcoinPrivateActions.PushTrade:
					await DispatchAsync(Deserialize<DeepcoinPrivateEnvelope<DeepcoinPrivateTrade>>(payload).Result,
						TradeReceived, cancellationToken);
					break;
				case DeepcoinPrivateActions.PushAccountDetail:
				case DeepcoinPrivateActions.PushTriggerOrder:
					break;
				case DeepcoinPrivateActions.Error:
					throw new InvalidOperationException(
						$"Deepcoin private WebSocket error {header.Code}: {header.Message}".Trim());
				default:
					throw new InvalidDataException("Deepcoin private WebSocket returned an unknown action.");
			}
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private static async ValueTask DispatchAsync<TData>(DeepcoinPrivateResult<TData>[] result,
		Func<TData, CancellationToken, ValueTask> handler, CancellationToken cancellationToken)
		where TData : class
	{
		if (handler is null)
			return;
		foreach (var item in result ?? [])
		{
			if (item is not null && item.Data is not null)
				await handler(item.Data, cancellationToken);
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings);
			if (result is null)
				throw new InvalidDataException("Deepcoin private WebSocket returned an empty message.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Deepcoin private WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
