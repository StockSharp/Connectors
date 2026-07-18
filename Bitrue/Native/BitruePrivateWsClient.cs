namespace StockSharp.Bitrue.Native;

sealed class BitruePrivateWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly string _listenKey;
	private readonly BitrueSections _section;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly HashSet<string> _channels = new(StringComparer.OrdinalIgnoreCase);
	private readonly SemaphoreSlim _sendSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private WebSocketClient _client;

	public BitruePrivateWsClient(string endpoint, string listenKey, BitrueSections section,
		WorkingTime workingTime, int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint));
		_listenKey = listenKey.ThrowIfEmpty(nameof(listenKey));
		_section = section;
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => nameof(Bitrue) + "_" + _section + "_PrivateWs";

	public event Func<BitrueSpotPrivateOrder, CancellationToken, ValueTask> SpotOrderReceived;
	public event Func<BitrueSpotPrivateBalanceEnvelope, CancellationToken, ValueTask> SpotBalanceReceived;
	public event Func<BitrueFuturesPrivateOrderEnvelope, CancellationToken, ValueTask> FuturesOrderReceived;
	public event Func<BitrueFuturesPrivateAccountEnvelope, CancellationToken, ValueTask> FuturesAccountReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;
	public event Func<BitrueSections, ConnectionStates, CancellationToken, ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		if (_client is not null)
			_client.PreProcess2 -= PreProcess;
		_client?.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_client is not null)
			throw new InvalidOperationException("Bitrue private WebSocket is already initialized.");
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

	public ValueTask SubscribeSpotOrdersAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_order_update", true, cancellationToken);

	public ValueTask UnsubscribeSpotOrdersAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_order_update", false, cancellationToken);

	public ValueTask SubscribeSpotBalancesAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_balance_update", true, cancellationToken);

	public ValueTask UnsubscribeSpotBalancesAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_balance_update", false, cancellationToken);

	public ValueTask SubscribeFuturesAccountAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_account_update", true, cancellationToken);

	public ValueTask UnsubscribeFuturesAccountAsync(CancellationToken cancellationToken)
		=> ChangeChannelAsync("user_account_update", false, cancellationToken);

	public ValueTask SendPongAsync(CancellationToken cancellationToken)
		=> _client is { IsConnected: true } client
			? SendAsync(client, new BitruePrivateWsPong
			{
				Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			}, cancellationToken)
			: default;

	private WebSocketClient CreateClient()
	{
		var path = _section == BitrueSections.Spot
			? "/stream?listenKey="
			: "/stream?streams=";
		var address = _endpoint.TrimEnd('/') + path + Uri.EscapeDataString(_listenKey);
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
			"StockSharp-Bitrue-Connector/1.0");
		client.PreProcess2 += PreProcess;
		return client;
	}

	private async ValueTask DisposeClientAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		if (client is null)
			return;
		client.PreProcess2 -= PreProcess;
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

	private async ValueTask OnStateChangedAsync(WebSocketClient client,
		ConnectionStates state, CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Restored)
			await SendChannelsAsync(client, true, cancellationToken);
		if (StateChanged is { } handler)
			await handler(_section, state, cancellationToken);
	}

	private async ValueTask ChangeChannelAsync(string channel, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		if ((_section == BitrueSections.Spot && channel == "user_account_update") ||
			(_section == BitrueSections.Futures && channel != "user_account_update"))
			throw new InvalidOperationException($"Channel '{channel}' is invalid for {_section}.");

		bool changed;
		using (_sync.EnterScope())
			changed = isSubscribe ? _channels.Add(channel) : _channels.Remove(channel);
		if (changed && _client?.IsConnected == true)
			await SendChannelAsync(_client, channel, isSubscribe, cancellationToken);
	}

	private async ValueTask SendChannelsAsync(WebSocketClient client, bool isSubscribe,
		CancellationToken cancellationToken)
	{
		string[] channels;
		using (_sync.EnterScope())
			channels = [.. _channels.OrderBy(static channel => channel)];
		foreach (var channel in channels)
			await SendChannelAsync(client, channel, isSubscribe, cancellationToken);
	}

	private ValueTask SendChannelAsync(WebSocketClient client, string channel,
		bool isSubscribe, CancellationToken cancellationToken)
		=> SendAsync(client, new BitrueWsCommand
		{
			Action = isSubscribe ? BitrueWsActions.Subscribe : BitrueWsActions.Unsubscribe,
			Parameters = new() { Channel = channel },
		}, cancellationToken);

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
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;

		try
		{
			var header = Deserialize<BitrueWsHeader>(payload);
			if (header.Event.EqualsIgnoreCase("ping"))
			{
				await SendAsync(client, new BitruePrivateWsPong
				{
					Timestamp = header.Timestamp,
				}, cancellationToken);
				return;
			}
			if (!header.Status.IsEmpty() && !header.Status.EqualsIgnoreCase("ok"))
				throw new InvalidOperationException(
					$"Bitrue private WebSocket request failed: {header.Status}".Trim());
			if (header.EventType.IsEmpty())
				return;

			if (_section == BitrueSections.Spot)
			{
				if (header.EventType.EqualsIgnoreCase("executionReport"))
				{
					if (SpotOrderReceived is { } orderHandler)
						await orderHandler(Deserialize<BitrueSpotPrivateOrder>(payload),
							cancellationToken);
				}
				else if (header.EventType.EqualsIgnoreCase("BALANCE") &&
					SpotBalanceReceived is { } balanceHandler)
					await balanceHandler(Deserialize<BitrueSpotPrivateBalanceEnvelope>(payload),
						cancellationToken);
			}
			else if (header.EventType.EqualsIgnoreCase("ORDER_TRADE_UPDATE"))
			{
				if (FuturesOrderReceived is { } orderHandler)
					await orderHandler(Deserialize<BitrueFuturesPrivateOrderEnvelope>(payload),
						cancellationToken);
			}
			else if (header.EventType.EqualsIgnoreCase("ACCOUNT_UPDATE") &&
				FuturesAccountReceived is { } accountHandler)
				await accountHandler(Deserialize<BitrueFuturesPrivateAccountEnvelope>(payload),
					cancellationToken);
		}
		catch (Exception error) when (error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private int PreProcess(ReadOnlyMemory<byte> source, Memory<byte> destination)
	{
		if (source.IsEmpty)
			return 0;
		var first = source.Span[0];
		if (first is (byte)'{' or (byte)'[')
		{
			source.CopyTo(destination);
			return source.Length;
		}
		return source.UnGzipTo(destination);
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			var result = JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings);
			if (result is null)
				throw new InvalidDataException(
					"Bitrue private WebSocket returned an empty message.");
			return result;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitrue private WebSocket returned malformed JSON.", error);
		}
	}

	private ValueTask RaiseErrorAsync(Exception error, CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
