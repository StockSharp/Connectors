namespace StockSharp.Benzinga;

public partial class BenzingaMessageAdapter
{
	private sealed class LiveNewsSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Symbol { get; init; }
		public HashSet<string> Channels { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Lock _liveSync = new();
	private readonly Dictionary<long, LiveNewsSubscription> _liveNews = [];
	private readonly SemaphoreSlim _streamStartLock = new(1, 1);

	private BenzingaRestClient _rest;
	private BenzingaNewsWebSocketClient _newsStream;

	/// <summary>Initializes a new instance.</summary>
	public BenzingaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedCandleTimeFrames(BenzingaExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BenzingaExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _newsStream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri || Address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException("Benzinga REST address must be an absolute HTTPS URI.");
		if (WebSocketAddress == null || !WebSocketAddress.IsAbsoluteUri ||
			!WebSocketAddress.Scheme.EqualsIgnoreCase("wss"))
		{
			throw new InvalidOperationException(
				"Benzinga News WebSocket address must be an absolute WSS URI.");
		}
		if (MaxNewsItems is <= 0 or > 100000)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxNewsItems), MaxNewsItems,
				"Benzinga news limit must be between 1 and 100000.");
		}
		if (MaxBars is <= 0 or > 100000)
		{
			throw new ArgumentOutOfRangeException(nameof(MaxBars), MaxBars,
				"Benzinga bars limit must be between 1 and 100000.");
		}

		var token = Token.UnSecure();
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, token, attempts) { Parent = this };
		try
		{
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null && _newsStream == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClients();
		ClearLiveSubscriptions();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClients();
		ClearLiveSubscriptions();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private BenzingaRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async Task EnsureNewsStream(CancellationToken cancellationToken)
	{
		await _streamStartLock.WaitAsync(cancellationToken);
		try
		{
			if (_newsStream?.IsStopped == false)
				return;
			if (_newsStream != null)
			{
				DetachStream(_newsStream);
				_newsStream.Dispose();
				_newsStream = null;
			}

			var token = Token?.UnSecure();
			token.ThrowIfEmpty(nameof(Token));
			var stream = new BenzingaNewsWebSocketClient(WebSocketAddress, token,
				NewsChannels, Math.Max(1, ReConnectionSettings.ReAttemptCount),
				HeartbeatInterval) { Parent = this };
			stream.NewsReceived += OnStreamNews;
			stream.Error += OnStreamError;
			_newsStream = stream;
			try
			{
				await stream.Connect(cancellationToken);
			}
			catch
			{
				DetachStream(stream);
				_newsStream = null;
				stream.Dispose();
				throw;
			}
		}
		finally
		{
			_streamStartLock.Release();
		}
	}

	private async Task DisposeClients()
	{
		var stream = _newsStream;
		_newsStream = null;
		if (stream != null)
		{
			DetachStream(stream);
			try
			{
				await stream.Disconnect();
			}
			finally
			{
				stream.Dispose();
			}
		}

		_rest?.Dispose();
		_rest = null;
	}

	private void DetachStream(BenzingaNewsWebSocketClient stream)
	{
		stream.NewsReceived -= OnStreamNews;
		stream.Error -= OnStreamError;
	}

	private async ValueTask OnStreamError(Exception error, bool isTerminal,
		CancellationToken cancellationToken)
	{
		await SendOutErrorAsync(error, cancellationToken);
		if (!isTerminal)
			return;

		long[] finished;
		using (var scope = _liveSync.EnterScope())
		{
			finished = [.. _liveNews.Keys];
			_liveNews.Clear();
		}
		foreach (var transactionId in finished)
			await SendSubscriptionFinishedAsync(transactionId, cancellationToken);
	}

	private async ValueTask OnStreamNews(BenzingaNewsStreamData data,
		CancellationToken cancellationToken)
	{
		var item = data?.Content;
		if (item == null || data.Action.EqualsIgnoreCase("deleted"))
			return;
		if (!TryGetNewsTime(data.Timestamp, item, out var serverTime))
			return;

		LiveNewsSubscription[] subscriptions;
		var finished = new HashSet<long>();
		using (var scope = _liveSync.EnterScope())
		{
			subscriptions = _liveNews.Values.Where(subscription =>
				item.Matches(subscription.Symbol, subscription.Channels)).ToArray();
			foreach (var subscription in subscriptions)
			{
				if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
				{
					_liveNews.Remove(subscription.TransactionId);
					finished.Add(subscription.TransactionId);
				}
			}
		}

		foreach (var subscription in subscriptions)
		{
			await SendNews(subscription.TransactionId, subscription.SecurityId,
				item, serverTime, cancellationToken);
			if (finished.Contains(subscription.TransactionId))
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		}
	}

	private static bool TryGetNewsTime(string envelopeTime, BenzingaNewsItem item,
		out DateTime result)
		=> BenzingaExtensions.TryParseUtc(envelopeTime, out result) ||
			BenzingaExtensions.TryParseUtc(item.Updated, out result) ||
			BenzingaExtensions.TryParseUtc(item.Created, out result);

	private void AddLiveSubscription(LiveNewsSubscription subscription)
	{
		using var scope = _liveSync.EnterScope();
		if (!_liveNews.TryAdd(subscription.TransactionId, subscription))
		{
			throw new InvalidOperationException(
				$"Benzinga news subscription {subscription.TransactionId} already exists.");
		}
	}

	private bool RemoveLiveSubscription(long transactionId)
	{
		using var scope = _liveSync.EnterScope();
		return _liveNews.Remove(transactionId);
	}

	private void ClearLiveSubscriptions()
	{
		using var scope = _liveSync.EnterScope();
		_liveNews.Clear();
	}
}
