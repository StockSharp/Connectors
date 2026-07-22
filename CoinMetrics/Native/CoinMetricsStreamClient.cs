namespace StockSharp.CoinMetrics.Native;

sealed class CoinMetricsStreamClient : BaseLogReceiver
{
	private readonly CoinMetricsStreamKey _key;
	private readonly string _apiKey;
	private readonly Uri _uri;
	private readonly WorkingTime _workingTime;
	private readonly int _reconnectAttempts;
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private readonly Lock _bookSync = new();
	private readonly SortedDictionary<decimal, decimal> _asks = [];
	private readonly SortedDictionary<decimal, decimal> _bids = [];
	private WebSocketClient _client;
	private long? _bookSequence;
	private bool _isBookInitialized;
	private bool _isDisposed;

	public CoinMetricsStreamClient(string endpoint, SecureString apiKey,
		CoinMetricsStreamKey key, WorkingTime workingTime, int reconnectAttempts)
	{
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_apiKey = apiKey.UnSecure().Trim();
		_key = ValidateKey(key);
		_uri = BuildUri(endpoint, _apiKey, _key);
		_workingTime = workingTime ?? throw new ArgumentNullException(
			nameof(workingTime));
		_reconnectAttempts = Math.Max(1, reconnectAttempts);
	}

	public override string Name => "CoinMetrics_WS_" + _key.Kind;

	public event Func<CoinMetricsStreamUpdate, CancellationToken, ValueTask>
		MessageReceived;
	public event Func<Exception, CancellationToken, ValueTask> Error;

	public async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		if (_client is not null)
			throw new InvalidOperationException(
				"Coin Metrics stream is already connected.");
		var client = new WebSocketClient(_uri.AbsoluteUri,
			OnStateChangedAsync, RaiseSanitizedErrorAsync, OnProcessAsync,
			static (_, _) => { }, static (_, _) => { }, static (_, _) => { })
		{
			ReconnectAttempts = _reconnectAttempts,
			WorkingTime = _workingTime,
			DisableAutoResend = true,
			Indent = false,
			SendSettings = _settings,
		};
		_client = client;
		try
		{
			await client.ConnectAsync(cancellationToken);
		}
		catch (Exception error)
		{
			_client = null;
			client.Dispose();
			if (cancellationToken.IsCancellationRequested)
				throw;
			throw new IOException(
				"Coin Metrics WebSocket connection failed: " +
				Redact(error.Message));
		}
	}

	private ValueTask OnStateChangedAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		this.AddInfoLog("Coin Metrics {0} stream state: {1}.", _key.Kind,
			state);
		if (state is ConnectionStates.Failed or ConnectionStates.Restored)
			ClearBook();
		return default;
	}

	private async ValueTask OnProcessAsync(WebSocketClient client,
		WebSocketMessage message, CancellationToken cancellationToken)
	{
		_ = client;
		var json = message.AsString();
		if (json.IsEmpty())
			return;
		try
		{
			var update = DeserializeUpdate(json);
			if (update is not null && MessageReceived is { } handler)
				await handler(update, cancellationToken);
		}
		catch (Exception error) when (error is JsonException or
			InvalidDataException or InvalidOperationException or FormatException or
			ArgumentException)
		{
			await RaiseSanitizedErrorAsync(error, cancellationToken);
		}
	}

	private CoinMetricsStreamUpdate DeserializeUpdate(string json)
	{
		switch (_key.Kind)
		{
			case CoinMetricsStreamKinds.Trades:
				var trade = Deserialize<CoinMetricsStreamTrade>(json);
				if (ProcessNotice(trade))
					return null;
				ValidateMarket(trade.Market);
				return new() { Key = _key, Trade = trade };
			case CoinMetricsStreamKinds.Quotes:
				var quote = Deserialize<CoinMetricsStreamQuote>(json);
				if (ProcessNotice(quote))
					return null;
				ValidateMarket(quote.Market);
				return new() { Key = _key, Quote = quote };
			case CoinMetricsStreamKinds.OrderBooks:
				var book = Deserialize<CoinMetricsStreamOrderBook>(json);
				if (ProcessNotice(book))
					return null;
				ValidateMarket(book.Market);
				return new() { Key = _key, OrderBook = ApplyBook(book) };
			case CoinMetricsStreamKinds.Candles:
				var candle = Deserialize<CoinMetricsStreamCandle>(json);
				if (ProcessNotice(candle))
					return null;
				ValidateMarket(candle.Market);
				return new() { Key = _key, Candle = candle };
			default:
				throw new InvalidDataException(
					"Coin Metrics stream kind is unsupported.");
		}
	}

	private bool ProcessNotice(CoinMetricsMarketRecord record)
	{
		if (record.Error is not null)
			throw new InvalidOperationException(
				"Coin Metrics stream error " +
				record.Error.Type.IsEmpty("unknown") + ": " +
				record.Error.Message.IsEmpty("request failed"));
		if (record.Warning is null)
			return false;
		this.AddWarningLog("Coin Metrics {0} stream warning {1}: {2}",
			_key.Kind, record.Warning.Type.IsEmpty("unknown"),
			Redact(record.Warning.Message.IsEmpty("unspecified warning")));
		return true;
	}

	private CoinMetricsStreamOrderBook ApplyBook(
		CoinMetricsStreamOrderBook message)
	{
		if (message.Type == CoinMetricsBookMessageTypes.Unknown ||
			message.Asks is null || message.Bids is null)
			throw new InvalidDataException(
				"Coin Metrics order-book message is incomplete.");
		_ = message.Time.ParseCoinMetricsTime("order-book");
		var sequence = message.SequenceId.ParseCoinMetricsSequence("order-book");
		using (_bookSync.EnterScope())
		{
			if (message.Type == CoinMetricsBookMessageTypes.Snapshot)
			{
				_asks.Clear();
				_bids.Clear();
				_isBookInitialized = true;
			}
			else
			{
				if (!_isBookInitialized)
					throw new InvalidDataException(
						"Coin Metrics order-book update arrived before a snapshot.");
				if (_bookSequence is { } previous && sequence != previous + 1)
					throw new InvalidDataException(
						$"Coin Metrics order-book sequence gap: {previous} to {sequence}.");
			}
			ApplyLevels(_asks, message.Asks, "ask");
			ApplyLevels(_bids, message.Bids, "bid");
			_bookSequence = sequence;
			message.Asks = [.. _asks.Select(static pair => new CoinMetricsBookLevel
			{
				Price = pair.Key,
				Size = pair.Value,
			})];
			message.Bids = [.. _bids.Select(static pair => new CoinMetricsBookLevel
			{
				Price = pair.Key,
				Size = pair.Value,
			})];
		}
		message.Type = CoinMetricsBookMessageTypes.Snapshot;
		return message;
	}

	private static void ApplyLevels(
		SortedDictionary<decimal, decimal> target,
		IEnumerable<CoinMetricsBookLevel> levels, string side)
	{
		foreach (var level in levels)
		{
			if (level?.Price is not > 0 || level.Size is null or < 0)
				throw new InvalidDataException(
					$"Coin Metrics {side} order-book level is invalid.");
			if (level.Size == 0)
				target.Remove(level.Price.Value);
			else
				target[level.Price.Value] = level.Size.Value;
		}
	}

	private T Deserialize<T>(string json)
	{
		var result = JsonConvert.DeserializeObject<T>(json, _settings);
		if (result is null)
			throw new InvalidDataException(
				"Coin Metrics WebSocket returned an empty JSON message.");
		return result;
	}

	private void ValidateMarket(string market)
	{
		if (!_key.Market.EqualsIgnoreCase(market))
			throw new InvalidDataException(
				"Coin Metrics WebSocket returned data for a different market.");
	}

	private void ClearBook()
	{
		using (_bookSync.EnterScope())
		{
			_asks.Clear();
			_bids.Clear();
			_bookSequence = null;
			_isBookInitialized = false;
		}
	}

	private ValueTask RaiseSanitizedErrorAsync(Exception error,
		CancellationToken cancellationToken)
		=> Error is { } handler
			? handler(new IOException(Redact(error.Message)), cancellationToken)
			: default;

	private string Redact(string value)
		=> value.IsEmpty()
			? value
			: value.Replace(_apiKey, "***", StringComparison.Ordinal);

	private static CoinMetricsStreamKey ValidateKey(CoinMetricsStreamKey key)
	{
		if (key.Kind == CoinMetricsStreamKinds.Unknown)
			throw new ArgumentException(
				"Coin Metrics stream kind is missing.", nameof(key));
		var market = CoinMetricsExtensions.NormalizeMarket(key.Market);
		if (key.Kind == CoinMetricsStreamKinds.Candles)
			_ = key.TimeFrame.ToFrequency();
		if ((key.Kind == CoinMetricsStreamKinds.OrderBooks &&
			 key.BookDepth is not (CoinMetricsBookDepthModes.Hundred or
				 CoinMetricsBookDepthModes.FullBook)) ||
			(key.Kind != CoinMetricsStreamKinds.OrderBooks &&
			 key.BookDepth != CoinMetricsBookDepthModes.Unknown))
			throw new ArgumentException(
				"Coin Metrics stream book-depth mode is invalid.", nameof(key));
		return key with { Market = market };
	}

	private static Uri BuildUri(string endpoint, string apiKey,
		CoinMetricsStreamKey key)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var root) || root.Scheme != "wss" ||
			root.Host.IsEmpty() || !root.UserInfo.IsEmpty() ||
			!root.Query.IsEmpty() || !root.Fragment.IsEmpty())
			throw new ArgumentException(
				"A valid WSS Coin Metrics API root is required.",
				nameof(endpoint));
		var query = new List<string>
		{
			"markets=" + Escape(key.Market),
			"backfill=" + (key.Kind == CoinMetricsStreamKinds.OrderBooks
				? CoinMetricsBackfillModes.Latest
				: CoinMetricsBackfillModes.None).ToWire(),
		};
		if (key.Kind == CoinMetricsStreamKinds.Quotes)
			query.Add("include_one_sided=true");
		else if (key.Kind == CoinMetricsStreamKinds.OrderBooks)
			query.Add("depth_limit=" + key.BookDepth.ToWire());
		else if (key.Kind == CoinMetricsStreamKinds.Candles)
			query.Add("frequency=" +
				Escape(key.TimeFrame.ToFrequency().ToWire()));
		query.Add("api_key=" + Escape(apiKey));
		var uri = new Uri(root, "timeseries-stream/" + key.Kind.ToPath() +
			"?" + string.Join('&', query));
		if (uri.AbsoluteUri.Length > 10000)
			throw new ArgumentException(
				"Coin Metrics WebSocket URL is too long.", nameof(key));
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	public async ValueTask DisconnectAsync(CancellationToken cancellationToken)
	{
		var client = _client;
		_client = null;
		ClearBook();
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

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
