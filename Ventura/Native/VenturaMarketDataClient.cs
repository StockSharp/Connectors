namespace StockSharp.Ventura.Native;

sealed class VenturaMarketDataClient : BaseLogReceiver
{
	private readonly WebSocketClient _client;
	private readonly SynchronizedDictionary<
		string,
		(string action, string token)> _subscriptions =
			new(StringComparer.OrdinalIgnoreCase);

	public VenturaMarketDataClient(
		Uri address,
		SecureString appKey,
		string clientId,
		SecureString authToken,
		int reconnectAttempts,
		WorkingTime workingTime)
	{
		var endpoint = AddCredentials(
			address ?? throw new ArgumentNullException(nameof(address)),
			appKey.ThrowIfEmpty(nameof(appKey)).UnSecure(),
			clientId.ThrowIfEmpty(nameof(clientId)),
			authToken.ThrowIfEmpty(nameof(authToken)).UnSecure());
		_client = new(
			endpoint.AbsoluteUri,
			(state, cancellationToken) =>
				StateChanged is { } stateHandler
					? stateHandler.InvokeAsync(state, cancellationToken)
					: default,
			(error, cancellationToken) =>
				Error is { } errorHandler
					? errorHandler.InvokeAsync(error, cancellationToken)
					: default,
			Process,
			(message, args) => this.AddInfoLog(message, args),
			(message, args) => this.AddErrorLog(message, args),
			(message, args) => this.AddVerboseLog(message, args))
		{
			ReconnectAttempts = reconnectAttempts,
			WorkingTime = workingTime,
			DisableAutoResend = true,
		};
		_client.InitAsync += OnInit;
		_client.PostConnect += OnPostConnect;
	}

	public override string Name => nameof(Ventura) + "_" +
		nameof(VenturaMarketDataClient);

	public event Func<VenturaMarketUpdate, CancellationToken, ValueTask>
		MarketDataReceived;

	public event Func<Exception, CancellationToken, ValueTask> Error;

	public event Func<ConnectionStates, CancellationToken, ValueTask>
		StateChanged;

	protected override void DisposeManaged()
	{
		_client.InitAsync -= OnInit;
		_client.PostConnect -= OnPostConnect;
		_client.Dispose();
		base.DisposeManaged();
	}

	public ValueTask Connect(CancellationToken cancellationToken)
		=> _client.ConnectAsync(cancellationToken);

	public ValueTask Disconnect(CancellationToken cancellationToken)
		=> _client.DisconnectAsync(cancellationToken);

	private ValueTask OnInit(
		ClientWebSocket socket,
		CancellationToken cancellationToken)
	{
		socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
		return default;
	}

	public async ValueTask Subscribe(
		string action,
		string token,
		CancellationToken cancellationToken)
	{
		action.ThrowIfEmpty(nameof(action));
		token.ThrowIfEmpty(nameof(token));
		var key = VenturaExtensions.CreateStreamKey(action, token);
		if (_subscriptions.ContainsKey(key))
			return;
		_subscriptions.Add(key, (action, token));
		try
		{
			await _client.SendAsync(
				CreateSubscriptionCommand(true, action, [token]),
				cancellationToken);
		}
		catch
		{
			_subscriptions.Remove(key);
			throw;
		}
	}

	public async ValueTask Unsubscribe(
		string action,
		string token,
		CancellationToken cancellationToken)
	{
		var key = VenturaExtensions.CreateStreamKey(action, token);
		if (!_subscriptions.Remove(key))
			return;
		await _client.SendAsync(
			CreateSubscriptionCommand(false, action, [token]),
			cancellationToken);
	}

	private async ValueTask OnPostConnect(
		bool reconnect,
		CancellationToken cancellationToken)
	{
		foreach (var group in _subscriptions.Values
			.GroupBy(
				item => item.action,
				StringComparer.OrdinalIgnoreCase))
		{
			foreach (var batch in group
				.Select(item => item.token)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.Chunk(1000))
			{
				await _client.SendAsync(
					CreateSubscriptionCommand(true, group.Key, batch),
					cancellationToken);
			}
		}
	}

	private async ValueTask Process(
		WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		var text = message.AsString()?.Trim();
		if (text.IsEmpty() || text.EqualsIgnoreCase("pong"))
			return;

		JToken response;
		try
		{
			response = JToken.Parse(text);
		}
		catch (JsonException error)
		{
			if (Error is { } invalidHandler)
			{
				await invalidHandler.InvokeAsync(
					new InvalidDataException(
						"Ventura EaseAPI market WebSocket returned invalid JSON.",
						error),
					cancellationToken);
			}
			return;
		}

		if (response is JObject status)
		{
			var value = VenturaRestClient.FindString(
				status,
				"message",
				"error",
				"detail");
			if (!value.IsEmpty() &&
				(value.Contains("error", StringComparison.OrdinalIgnoreCase) ||
					value.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
					value.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
			{
				if (Error is { } errorHandler)
				{
					await errorHandler.InvokeAsync(
						new InvalidOperationException(
							$"Ventura EaseAPI market WebSocket: {value}"),
						cancellationToken);
				}
			}
			return;
		}

		if (MarketDataReceived is not { } handler)
			return;
		foreach (var update in Decode(text, DateTime.UtcNow))
			await handler.InvokeAsync(update, cancellationToken);
	}

	internal static string CreateSubscriptionCommand(
		bool subscribe,
		string action,
		IEnumerable<string> tokens)
	{
		action.ThrowIfEmpty(nameof(action));
		var values = tokens?
			.Where(token => !token.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray() ??
			throw new ArgumentNullException(nameof(tokens));
		if (values.Length == 0)
			throw new ArgumentOutOfRangeException(nameof(tokens));
		return new JObject
		{
			["actions"] = new JArray(action),
			["token"] = new JArray(values),
			["mode"] = subscribe ? "sub" : "unsub",
		}.ToString(Formatting.None);
	}

	internal static VenturaMarketUpdate[] Decode(
		string json,
		DateTime fallback)
	{
		JToken token;
		try
		{
			token = JToken.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Ventura EaseAPI market WebSocket returned invalid JSON.",
				error);
		}
		if (token is not JArray array)
			return [];
		var rows = array.FirstOrDefault() is JArray
			? array.OfType<JArray>()
			: [array];
		return
		[
			..
				rows
					.Select(row => ParseRow(row, fallback))
					.Where(update => update != null)
		];
	}

	private static VenturaMarketUpdate ParseRow(
		JArray row,
		DateTime fallback)
	{
		if (row == null || row.Count < 8)
			return null;
		var action = row[0]?.Value<string>();
		var token = row[1]?.Value<string>();
		if (action.IsEmpty() || token.IsEmpty())
			return null;
		var isIndex = action.StartsWith(
			"index:",
			StringComparison.OrdinalIgnoreCase);
		var depth = !isIndex &&
			action.EndsWith(
				":ltp_depth",
				StringComparison.OrdinalIgnoreCase);
		if (!isIndex && row.Count < 9)
			return null;
		return new()
		{
			Action = action,
			Token = token,
			LastPrice = VenturaRestClient.DecimalAt(row, 2),
			OpenPrice = VenturaRestClient.DecimalAt(row, 3),
			HighPrice = VenturaRestClient.DecimalAt(row, 4),
			LowPrice = VenturaRestClient.DecimalAt(row, 5),
			PreviousClose = VenturaRestClient.DecimalAt(row, 6),
			Volume = isIndex
				? 0m
				: VenturaRestClient.DecimalAt(row, 7),
			ServerTime = VenturaRestClient.ParseTime(
				row[isIndex ? 7 : 8],
				fallback),
			TotalBuyQuantity = depth
				? VenturaRestClient.DecimalAt(row, 9)
				: 0m,
			TotalSellQuantity = depth
				? VenturaRestClient.DecimalAt(row, 10)
				: 0m,
			Depth = depth && row.Count > 11 && row[11] is JArray levels
				? VenturaRestClient.ParseDepth(levels)
				: [],
		};
	}

	internal static Uri AddCredentials(
		Uri address,
		string appKey,
		string clientId,
		string authToken)
	{
		var builder = new UriBuilder(address);
		var query = builder.Query.TrimStart('?');
		var credentials =
			$"app_key={Uri.EscapeDataString(appKey)}" +
			$"&client_id={Uri.EscapeDataString(clientId)}" +
			$"&authorization={Uri.EscapeDataString(authToken)}";
		builder.Query =
			(query.IsEmpty() ? string.Empty : query + "&") +
			credentials;
		return builder.Uri;
	}
}
