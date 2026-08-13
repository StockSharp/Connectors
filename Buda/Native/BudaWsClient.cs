namespace StockSharp.Buda.Native;

sealed class BudaWsClient : BaseLogReceiver
{
	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, WebSocketClient> _clients =
		new(StringComparer.OrdinalIgnoreCase);
	private bool _isConnected;

	public BudaWsClient(
		string endpoint,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(
			nameof(endpoint)).TrimEnd('/');
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Buda_WS";

	public event Func<
		BudaWsMessage,
		CancellationToken,
		ValueTask> MessageReceived;

	public event Func<
		Exception,
		CancellationToken,
		ValueTask> Error;

	public event Func<
		ConnectionStates,
		CancellationToken,
		ValueTask> StateChanged;

	protected override void DisposeManaged()
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
		}
		foreach (var client in clients)
			client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask ConnectAsync(
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		using (_sync.EnterScope())
		{
			if (_isConnected)
				throw new InvalidOperationException(
					"Buda.com WebSocket manager is already connected.");
			_isConnected = true;
		}
		return default;
	}

	public async ValueTask DisconnectAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient[] clients;
		using (_sync.EnterScope())
		{
			_isConnected = false;
			clients = [.. _clients.Values];
			_clients.Clear();
		}
		foreach (var client in clients)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public async ValueTask SubscribeAsync(
		string channel,
		CancellationToken cancellationToken)
	{
		channel = NormalizeChannel(channel);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_isConnected)
				throw new InvalidOperationException(
					"Buda.com WebSocket manager is disconnected.");
			if (_clients.ContainsKey(channel))
				return;
			client = CreateClient(channel);
			_clients.Add(channel, client);
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_clients.Remove(channel);
			client.Dispose();
			throw;
		}
	}

	public async ValueTask UnsubscribeAsync(
		string channel,
		CancellationToken cancellationToken)
	{
		channel = NormalizeChannel(channel);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_clients.Remove(channel, out client))
				return;
		}
		await DisconnectClientAsync(client, cancellationToken);
	}

	private WebSocketClient CreateClient(string channel)
	{
		var client = new WebSocketClient(
			CreateChannelEndpoint(_endpoint, channel),
			(state, token) => RaiseStateAsync(state, token),
			(error, token) => RaiseErrorAsync(error, token),
			(_, message, token) => OnProcessAsync(message, token),
			(s, a) => this.AddInfoLog(s, a),
			(s, a) => this.AddErrorLog(s, a),
			(s, a) => this.AddVerboseLog(s, a))
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
		};
		client.InitAsync += (socket, _) =>
		{
			socket.Options.SetRequestHeader(
				"User-Agent",
				"StockSharp-Buda-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var value = DeserializeMessage(payload);
			if (MessageReceived is { } handler)
				await handler.InvokeAsync(value, cancellationToken);
		}
		catch (Exception error) when (
			error is JsonException or InvalidDataException or
			InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	internal static BudaWsMessage DeserializeMessage(string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			var eventName = root.Value<string>("ev");
			var marketId = root.Value<string>("mk");
			return new()
			{
				Event = eventName,
				MarketId = marketId,
				Time = ParseEventTime(root.Value<string>("ts")),
				Trade = root["trade"] is JArray trade
					? BudaRestClient.ParseTrade(trade, marketId)
					: null,
				OrderBook = root["order_book"] is JObject book
					? BudaRestClient.ParseOrderBook(book)
					: null,
				Change = ParseChange(root["change"] as JArray),
				Balance = root["balance"] is JObject balance
					? BudaRestClient.ParseBalance(balance)
					: null,
				Order = root["order"] is JObject order
					? BudaRestClient.ParseOrder(order)
					: null,
			};
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Buda.com WebSocket returned malformed JSON.",
				error);
		}
	}

	internal static string CreateChannelEndpoint(
		string endpoint,
		string channel)
		=> endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/') +
			"?channel=" +
			Uri.EscapeDataString(NormalizeChannel(channel))
				.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

	private static BudaBookChange ParseChange(JArray value)
	{
		if (value is not { Count: >= 3 })
			return null;
		return new()
		{
			Side = value[0].Value<string>().ToSide(),
			Price = ReadDecimal(value[1]),
			Delta = ReadDecimal(value[2]),
		};
	}

	private static DateTime? ParseEventTime(string value)
	{
		if (!decimal.TryParse(
			value,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var seconds))
			return null;
		var milliseconds = decimal.ToInt64(
			decimal.Truncate(seconds * 1000));
		return DateTimeOffset.FromUnixTimeMilliseconds(
			milliseconds).UtcDateTime;
	}

	private static decimal ReadDecimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler.InvokeAsync(error, cancellationToken)
			: default;

	private ValueTask RaiseStateAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler.InvokeAsync(state, cancellationToken)
			: default;

	private static async ValueTask DisconnectClientAsync(
		WebSocketClient client,
		CancellationToken cancellationToken)
	{
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

	private static string NormalizeChannel(string channel)
		=> channel.ThrowIfEmpty(nameof(channel)).Trim();
}
