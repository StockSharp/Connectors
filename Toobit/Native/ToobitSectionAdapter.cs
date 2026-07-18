namespace StockSharp.Toobit.Native;

sealed partial class ToobitSectionAdapter : BaseNativeAdapter
{
	private static readonly TimeSpan _listenKeyRefreshInterval = TimeSpan.FromMinutes(25);
	private static readonly TimeSpan _webSocketPingInterval = TimeSpan.FromMinutes(2);
	private readonly bool _isFutures;
	private readonly string _restEndpoint;
	private readonly string _wsEndpoint;
	private readonly WorkingTime _workingTime;
	private readonly Lock _sync = new();
	private readonly Dictionary<long, string> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _level1References = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _depthReferences = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _tickReferences = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(string Symbol, TimeSpan TimeFrame), int> _candleReferences = [];
	private ToobitRestClient _restClient;
	private ToobitWsClient _wsClient;
	private ToobitWsClient _userWsClient;
	private string _portfolioName;
	private string _listenKey;
	private DateTime _nextListenKeyRefresh;
	private DateTime _nextWebSocketPing;

	private sealed class DepthSubscription
	{
		public string Symbol { get; init; }
		public int? MaxDepth { get; init; }
	}

	private sealed class TickSubscription
	{
		public string Symbol { get; init; }
		public long TransactionId { get; init; }
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription
	{
		public string Symbol { get; init; }
		public long TransactionId { get; init; }
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	public ToobitSectionAdapter(SecureString key, SecureString secret, bool isFutures,
		string restEndpoint, string wsEndpoint, WorkingTime workingTime)
		: base(key, secret,
			isFutures ? BoardCodes.ToobitFutures : BoardCodes.Toobit,
			isFutures ? SecurityTypes.Future : SecurityTypes.CryptoCurrency,
			isFutures ? "Futures" : "Spot")
	{
		_isFutures = isFutures;
		_restEndpoint = restEndpoint.ThrowIfEmpty(nameof(restEndpoint));
		_wsEndpoint = wsEndpoint.ThrowIfEmpty(nameof(wsEndpoint));
		_workingTime = workingTime;
	}

	private ToobitRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private ToobitWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	public override async ValueTask ConnectAsync(CancellationToken cancellationToken)
	{
		if (_restClient is not null || _wsClient is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_restClient = new(_restEndpoint, Key, Secret) { Parent = this };
		await _restClient.SynchronizeTimeAsync(cancellationToken);

		_wsClient = new(_wsEndpoint, false, _workingTime) { Parent = this };
		_wsClient.TickerReceived += OnTickerAsync;
		_wsClient.DepthReceived += OnDepthAsync;
		_wsClient.TradeReceived += OnTradeAsync;
		_wsClient.CandleReceived += OnCandleAsync;
		_wsClient.Error += OnWebSocketErrorAsync;
		await _wsClient.ConnectAsync(cancellationToken);

		_portfolioName = $"Toobit_{SectionName}_{(Key.IsEmpty() ? "Public" : Key.ToId())}";
		_nextWebSocketPing = DateTime.UtcNow + _webSocketPingInterval;
		ClearSubscriptions();

		if (!Key.IsEmpty() && !Secret.IsEmpty())
			await ConnectUserStreamAsync(cancellationToken);
	}

	public override void Disconnect()
	{
		if (!_listenKey.IsEmpty() && _restClient is not null)
		{
			try
			{
				_restClient.DeleteListenKeyAsync(_isFutures, _listenKey, default)
					.AsTask().GetAwaiter().GetResult();
			}
			catch (Exception error)
			{
				this.AddErrorLog(error);
			}
		}

		DisconnectUserStream();
		ClearSubscriptions();

		if (_wsClient is not null)
		{
			_wsClient.TickerReceived -= OnTickerAsync;
			_wsClient.DepthReceived -= OnDepthAsync;
			_wsClient.TradeReceived -= OnTradeAsync;
			_wsClient.CandleReceived -= OnCandleAsync;
			_wsClient.Error -= OnWebSocketErrorAsync;
			_wsClient.DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
			_wsClient.Dispose();
			_wsClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		_portfolioName = null;
		_nextWebSocketPing = default;
	}

	public override async ValueTask ResetAsync(CancellationToken cancellationToken)
	{
		await ReleaseListenKeyAsync(cancellationToken);
		Disconnect();
	}

	public override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		_ = timeMsg;
		var now = DateTime.UtcNow;

		if (_wsClient is not null && now >= _nextWebSocketPing)
		{
			await _wsClient.PingAsync(cancellationToken);
			if (_userWsClient is not null)
				await _userWsClient.PingAsync(cancellationToken);
			_nextWebSocketPing = now + _webSocketPingInterval;
		}

		if (!_listenKey.IsEmpty() && now >= _nextListenKeyRefresh)
		{
			await RestClient.KeepAliveListenKeyAsync(_isFutures, _listenKey, cancellationToken);
			_nextListenKeyRefresh = now + _listenKeyRefreshInterval;
		}
	}

	private async ValueTask ConnectUserStreamAsync(CancellationToken cancellationToken)
	{
		_listenKey = await RestClient.CreateListenKeyAsync(_isFutures, cancellationToken);
		_nextListenKeyRefresh = DateTime.UtcNow + _listenKeyRefreshInterval;
		_userWsClient = new(BuildUserWebSocketEndpoint(_wsEndpoint, _listenKey), true, _workingTime)
		{
			Parent = this,
		};
		_userWsClient.BalanceReceived += OnBalanceAsync;
		_userWsClient.PositionReceived += OnPositionAsync;
		_userWsClient.OrderReceived += OnOrderAsync;
		_userWsClient.UserTradeReceived += OnUserTradeAsync;
		_userWsClient.ListenKeyExpiring += OnListenKeyExpiringAsync;
		_userWsClient.Error += OnWebSocketErrorAsync;
		await _userWsClient.ConnectAsync(cancellationToken);
	}

	private void DisconnectUserStream()
	{
		if (_userWsClient is not null)
		{
			_userWsClient.BalanceReceived -= OnBalanceAsync;
			_userWsClient.PositionReceived -= OnPositionAsync;
			_userWsClient.OrderReceived -= OnOrderAsync;
			_userWsClient.UserTradeReceived -= OnUserTradeAsync;
			_userWsClient.ListenKeyExpiring -= OnListenKeyExpiringAsync;
			_userWsClient.Error -= OnWebSocketErrorAsync;
			_userWsClient.DisconnectAsync(default).AsTask().GetAwaiter().GetResult();
			_userWsClient.Dispose();
			_userWsClient = null;
		}

		_listenKey = null;
		_nextListenKeyRefresh = default;
	}

	private async ValueTask ReleaseListenKeyAsync(CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty() || _restClient is null)
			return;

		var listenKey = _listenKey;
		_listenKey = null;
		_nextListenKeyRefresh = default;
		try
		{
			await _restClient.DeleteListenKeyAsync(_isFutures, listenKey, cancellationToken);
		}
		catch (Exception error)
		{
			if (!cancellationToken.IsCancellationRequested)
				await SendOutErrorAsync(error, cancellationToken);
		}
	}

	private async ValueTask OnListenKeyExpiringAsync(ToobitListenKeyExpiry expiry,
		CancellationToken cancellationToken)
	{
		if (_listenKey.IsEmpty() || !expiry.ListenKey.EqualsIgnoreCase(_listenKey))
			return;

		await RestClient.KeepAliveListenKeyAsync(_isFutures, _listenKey, cancellationToken);
		_nextListenKeyRefresh = DateTime.UtcNow + _listenKeyRefreshInterval;
	}

	private ValueTask OnWebSocketErrorAsync(Exception error, CancellationToken cancellationToken)
		=> SendOutErrorAsync(error, cancellationToken);

	private void EnsurePrivateReady()
	{
		if (Key.IsEmpty() || Secret.IsEmpty())
			throw new InvalidOperationException("Toobit API key and secret are required for private operations.");
	}

	private void ClearSubscriptions()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_level1References.Clear();
			_depthReferences.Clear();
			_tickReferences.Clear();
			_candleReferences.Clear();
		}
	}

	private static bool AddReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}

		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (!references.TryGetValue(key, out var count))
			return false;
		if (count > 1)
		{
			references[key] = count - 1;
			return false;
		}

		references.Remove(key);
		return true;
	}

	private static string BuildUserWebSocketEndpoint(string publicEndpoint, string listenKey)
	{
		var uri = new Uri(publicEndpoint, UriKind.Absolute);
		return new UriBuilder(uri)
		{
			Path = "/api/v1/ws/" + Uri.EscapeDataString(listenKey),
			Query = string.Empty,
		}.Uri.ToString().TrimEnd('/');
	}
}
