namespace StockSharp.Bitkub.Native;

sealed class BitkubPublicWebSocketClient : BaseLogReceiver
{
	private sealed class PublicStream
	{
		public string Symbol { get; init; }
		public int PairingId { get; init; }
		public int ReferenceCount { get; set; }
		public WebSocketClient Client { get; set; }
	}

	private readonly string _endpoint;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, PublicStream> _streams =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};

	public BitkubPublicWebSocketClient(string endpoint, WorkingTime workingTime,
		int reconnectAttempts)
	{
		_endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		_workingTime = workingTime ?? throw new ArgumentNullException(nameof(workingTime));
		_reconnectAttempts = reconnectAttempts;
	}

	public override string Name => "Bitkub_PublicWs";

	public event Func<string, BitkubWebSocketChangedData, CancellationToken,
		ValueTask> TradesChanged;
	public event Func<string, BitkubWebSocketDepth, CancellationToken,
		ValueTask> DepthChanged;
	public event Func<string, BitkubTicker, CancellationToken,
		ValueTask> TickerChanged;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	protected override void DisposeManaged()
	{
		PublicStream[] streams;
		using (_sync.EnterScope())
		{
			streams = [.. _streams.Values];
			_streams.Clear();
		}
		foreach (var stream in streams)
			stream.Client?.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SubscribeAsync(string symbol, int pairingId,
		CancellationToken cancellationToken)
	{
		symbol = symbol.NormalizeSymbol();
		PublicStream stream;
		using (_sync.EnterScope())
		{
			if (_streams.TryGetValue(symbol, out stream))
			{
				stream.ReferenceCount++;
				return;
			}
			stream = new()
			{
				Symbol = symbol,
				PairingId = pairingId,
				ReferenceCount = 1,
			};
			stream.Client = CreateClient(stream);
			_streams.Add(symbol, stream);
		}

		try
		{
			await stream.Client.ConnectAsync(cancellationToken);
		}
		catch
		{
			using (_sync.EnterScope())
				if (_streams.TryGetValue(symbol, out var current) &&
					ReferenceEquals(current, stream))
					_streams.Remove(symbol);
			stream.Client.Dispose();
			throw;
		}
	}

	public async ValueTask ReleaseAsync(string symbol,
		CancellationToken cancellationToken)
	{
		symbol = symbol.NormalizeSymbol();
		PublicStream stream = null;
		using (_sync.EnterScope())
		{
			if (!_streams.TryGetValue(symbol, out var current))
				return;
			if (--current.ReferenceCount > 0)
				return;
			_streams.Remove(symbol);
			stream = current;
		}
		await DisconnectStreamAsync(stream, cancellationToken);
	}

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		PublicStream[] streams;
		using (_sync.EnterScope())
		{
			streams = [.. _streams.Values];
			_streams.Clear();
		}
		foreach (var stream in streams)
			await DisconnectStreamAsync(stream, cancellationToken);
	}

	private WebSocketClient CreateClient(PublicStream stream)
	{
		WebSocketClient client = null;
		client = new WebSocketClient(
			$"{_endpoint}/orderbook/{stream.PairingId}",
			(state, token) => OnStateChangedAsync(stream, state, token),
			(error, token) => RaiseErrorAsync(new InvalidOperationException(
				$"Bitkub WebSocket stream '{stream.Symbol}' failed.", error), token),
			(socket, message, token) => OnProcessAsync(stream, socket, message, token),
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
			"StockSharp-Bitkub-Connector/1.0");
		return client;
	}

	private ValueTask OnStateChangedAsync(PublicStream stream,
		ConnectionStates state, CancellationToken cancellationToken)
		=> state == ConnectionStates.Failed
			? RaiseErrorAsync(new InvalidOperationException(
				$"Bitkub WebSocket stream '{stream.Symbol}' disconnected."),
				cancellationToken)
			: default;

	private async ValueTask OnProcessAsync(PublicStream stream,
		WebSocketClient client, WebSocketMessage message,
		CancellationToken cancellationToken)
	{
		_ = client;
		var payload = message.AsString();
		if (payload.IsEmpty())
			return;
		try
		{
			var header = Deserialize<BitkubPublicWebSocketHeader>(payload);
			switch (header.Event)
			{
				case BitkubPublicWebSocketEvents.TradesChanged:
				{
					var envelope = Deserialize<BitkubPublicWebSocketEnvelope<
						BitkubWebSocketChangedData>>(payload);
					if (envelope.Data is not null && TradesChanged is { } handler)
						await handler(stream.Symbol, envelope.Data, cancellationToken);
					return;
				}
				case BitkubPublicWebSocketEvents.DepthChanged:
				{
					var envelope = Deserialize<BitkubPublicWebSocketEnvelope<
						BitkubWebSocketDepth>>(payload);
					if (envelope.Data is not null && DepthChanged is { } handler)
						await handler(stream.Symbol, envelope.Data, cancellationToken);
					return;
				}
				case BitkubPublicWebSocketEvents.Ticker:
				case BitkubPublicWebSocketEvents.GlobalTicker:
				{
					var envelope = Deserialize<BitkubPublicWebSocketEnvelope<
						BitkubTicker>>(payload);
					if (envelope.Data is null ||
						envelope.Data.PairingId is int pairingId &&
						pairingId != stream.PairingId)
						return;
					if (TickerChanged is { } handler)
						await handler(stream.Symbol, envelope.Data, cancellationToken);
					return;
				}
				case BitkubPublicWebSocketEvents.BidsChanged:
				case BitkubPublicWebSocketEvents.AsksChanged:
					return;
				default:
					throw new InvalidDataException(
						"Bitkub WebSocket returned an unsupported event.");
			}
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			OverflowException)
		{
			await RaiseErrorAsync(error, cancellationToken);
		}
	}

	private TMessage Deserialize<TMessage>(string payload)
	{
		try
		{
			return JsonConvert.DeserializeObject<TMessage>(payload, _jsonSettings)
				?? throw new InvalidDataException(
					"Bitkub WebSocket returned an empty message.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Bitkub WebSocket returned malformed JSON.", error);
		}
	}

	private static async ValueTask DisconnectStreamAsync(PublicStream stream,
		CancellationToken cancellationToken)
	{
		try
		{
			if (stream.Client?.IsConnected == true)
				await stream.Client.DisconnectAsync(cancellationToken);
		}
		finally
		{
			stream.Client?.Dispose();
		}
	}

	private ValueTask RaiseErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler ? handler(error, cancellationToken) : default;
}
