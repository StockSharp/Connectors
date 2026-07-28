namespace StockSharp.Coinmetro.Native;

sealed class CoinmetroWsClient : BaseLogReceiver
{
	private enum StreamKinds
	{
		Ticks,
		Book,
		Private,
	}

	private readonly string _endpoint;
	private readonly string _token;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, WebSocketClient>
		_bookClients =
			new(StringComparer.OrdinalIgnoreCase);
	private WebSocketClient _tickClient;
	private WebSocketClient _privateClient;
	private bool _isConnected;

	public CoinmetroWsClient(
		string endpoint,
		string token,
		WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_token = token?.Trim();
		_workingTime = workingTime ??
			throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Coinmetro_WS";

	public event Func<
		CoinmetroWsMessage,
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
			clients = GetClients();
			_tickClient = null;
			_privateClient = null;
			_bookClients.Clear();
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
					"Coinmetro WebSocket manager is already " +
						"connected.");
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
			clients = GetClients();
			_tickClient = null;
			_privateClient = null;
			_bookClients.Clear();
		}
		foreach (var client in clients)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public async ValueTask SubscribeTicksAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			EnsureConnected();
			if (_tickClient is not null)
				return;
			client = CreateClient(null, null, StreamKinds.Ticks);
			_tickClient = client;
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (ReferenceEquals(_tickClient, client))
					_tickClient = null;
			}
			client.Dispose();
			throw;
		}
	}

	public async ValueTask UnsubscribeTicksAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			client = _tickClient;
			_tickClient = null;
		}
		if (client is not null)
			await DisconnectClientAsync(client, cancellationToken);
	}

	public async ValueTask SubscribeBookAsync(
		string pair,
		CancellationToken cancellationToken)
	{
		pair = NormalizePair(pair);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			EnsureConnected();
			if (_bookClients.ContainsKey(pair))
				return;
			client = CreateClient(pair, null, StreamKinds.Book);
			_bookClients.Add(pair, client);
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				_bookClients.Remove(pair);
			client.Dispose();
			throw;
		}
	}

	public async ValueTask UnsubscribeBookAsync(
		string pair,
		CancellationToken cancellationToken)
	{
		pair = NormalizePair(pair);
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			if (!_bookClients.Remove(pair, out client))
				return;
		}
		await DisconnectClientAsync(client, cancellationToken);
	}

	public async ValueTask SubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		if (_token.IsEmpty())
			throw new InvalidOperationException(
				"Coinmetro bearer token is required for private " +
					"WebSocket events.");
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			EnsureConnected();
			if (_privateClient is not null)
				return;
			client = CreateClient(
				null, _token, StreamKinds.Private);
			_privateClient = client;
		}
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
			{
				if (ReferenceEquals(_privateClient, client))
					_privateClient = null;
			}
			client.Dispose();
			throw;
		}
	}

	public async ValueTask UnsubscribePrivateAsync(
		CancellationToken cancellationToken)
	{
		WebSocketClient client;
		using (_sync.EnterScope())
		{
			client = _privateClient;
			_privateClient = null;
		}
		if (client is not null)
			await DisconnectClientAsync(client, cancellationToken);
	}

	internal static string CreateEndpoint(
		string endpoint,
		string pair,
		string token)
	{
		endpoint = ValidateEndpoint(endpoint);
		var query = new List<string>();
		if (!token.IsEmpty())
			query.Add("token=" + Uri.EscapeDataString(token.Trim()));
		if (!pair.IsEmpty())
			query.Add("pairs=" +
				Uri.EscapeDataString(NormalizePair(pair)));
		return query.Count == 0
			? endpoint
			: endpoint + (endpoint.Contains('?') ? "&" : "?") +
				query.Join("&");
	}

	internal static CoinmetroWsMessage DeserializeMessage(
		string payload)
	{
		try
		{
			var root = JObject.Parse(
				payload.ThrowIfEmpty(nameof(payload)));
			if (root["error"] is not null ||
				root.Value<string>("status")
					.EqualsIgnoreCase("fail"))
				throw new InvalidDataException(
					"Coinmetro WebSocket request failed: " +
					(root["error"]?.ToString() ??
						root.Value<string>("reason") ??
						root.Value<string>("message")));
			return new()
			{
				Tick = root["tick"] is JObject tick
					? CoinmetroRestClient.ParseTicker(tick)
					: null,
				BookUpdate =
					root["bookUpdate"] is JObject book
						? CoinmetroRestClient.ParseBookUpdate(book)
						: null,
				OrderStatus =
					root["orderStatus"] as JObject,
				WalletUpdate =
					root["walletUpdate"] is JObject wallet
						? CoinmetroRestClient.ParseWallet(wallet)
						: null,
			};
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinmetro WebSocket returned malformed JSON.",
				error);
		}
	}

	private WebSocketClient CreateClient(
		string pair,
		string token,
		StreamKinds kind)
	{
		var client = new WebSocketClient(
			CreateEndpoint(_endpoint, pair, token),
			(state, cancellationToken) =>
				RaiseStateAsync(state, cancellationToken),
			(error, cancellationToken) =>
				RaiseErrorAsync(error, cancellationToken),
			(_, message, cancellationToken) =>
				OnProcessAsync(
					message, kind, cancellationToken),
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
				"StockSharp-Coinmetro-Connector/1.0");
			return default;
		};
		return client;
	}

	private async ValueTask OnProcessAsync(
		WebSocketMessage message,
		StreamKinds kind,
		CancellationToken cancellationToken)
	{
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var parsed = DeserializeMessage(payload);
			var accepted = kind switch
			{
				StreamKinds.Ticks => parsed.Tick is not null,
				StreamKinds.Book =>
					parsed.BookUpdate is not null,
				StreamKinds.Private =>
					parsed.OrderStatus is not null ||
					parsed.WalletUpdate is not null,
				_ => false,
			};
			if (accepted && MessageReceived is { } handler)
				await handler(parsed, cancellationToken);
		}
		catch (Exception error) when (
			error is JsonException or InvalidDataException or
				InvalidOperationException or FormatException or
				OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private WebSocketClient[] GetClients()
		=> [.. _bookClients.Values
			.Concat(_tickClient is null
				? []
				: [_tickClient])
			.Concat(_privateClient is null
				? []
				: [_privateClient])
			.Distinct()];

	private void EnsureConnected()
	{
		if (!_isConnected)
			throw new InvalidOperationException(
				"Coinmetro WebSocket manager is disconnected.");
	}

	private ValueTask RaiseErrorAsync(
		Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(error, cancellationToken)
			: default;

	private ValueTask RaiseStateAsync(
		ConnectionStates state,
		CancellationToken cancellationToken)
		=> StateChanged is { } handler
			? handler(state, cancellationToken)
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

	private static string NormalizePair(string pair)
		=> pair.ThrowIfEmpty(nameof(pair))
			.Trim().Replace("/", string.Empty)
			.Replace("-", string.Empty)
			.Replace("_", string.Empty)
			.ToUpperInvariant();

	private static string ValidateEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(
			nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(
			endpoint,
			UriKind.Absolute,
			out var value) ||
			!value.Scheme.EqualsIgnoreCase("wss"))
			throw new ArgumentException(
				"Coinmetro WebSocket endpoint must be an " +
					"absolute WSS URI.",
				nameof(endpoint));
		return endpoint;
	}
}
