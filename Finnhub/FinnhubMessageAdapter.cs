namespace StockSharp.Finnhub;

public partial class FinnhubMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Symbol { get; init; }
		public DataType DataType { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly object _liveSync = new();
	private FinnhubRestClient _rest;
	private FinnhubWebSocketClient _stream;

	/// <summary>Initializes a new instance.</summary>
	public FinnhubMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[Extensions.StockBoard, Extensions.ForexBoard, Extensions.CryptoBoard];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Ticks ||
			dataType == DataType.News || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri || Address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException("Finnhub REST address must be an absolute HTTPS URI.");
		if (WebSocketAddress == null || !WebSocketAddress.IsAbsoluteUri ||
			!WebSocketAddress.Scheme.EqualsIgnoreCase("wss"))
			throw new InvalidOperationException("Finnhub WebSocket address must be an absolute WSS URI.");
		StockExchange.ThrowIfEmpty(nameof(StockExchange));
		ForexExchange.ThrowIfEmpty(nameof(ForexExchange));
		CryptoExchange.ThrowIfEmpty(nameof(CryptoExchange));

		var token = Token.UnSecure();
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, token, attempts) { Parent = this };
		_stream = new(WebSocketAddress, token, attempts) { Parent = this };
		_stream.TradeReceived += OnStreamTrade;
		_stream.Error += SendOutErrorAsync;
		_stream.StateChanged += SendOutConnectionStateAsync;

		try
		{
			await _rest.Validate(cancellationToken);
			await _stream.Connect(cancellationToken);
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
		if (_rest == null && _stream == null)
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

	private async Task DisposeClients()
	{
		var stream = _stream;
		_stream = null;
		if (stream != null)
		{
			stream.TradeReceived -= OnStreamTrade;
			stream.Error -= SendOutErrorAsync;
			stream.StateChanged -= SendOutConnectionStateAsync;
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

	private void ClearLiveSubscriptions()
	{
		lock (_liveSync)
			_liveSubscriptions.Clear();
	}

	private FinnhubRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private FinnhubWebSocketClient SafeStream()
		=> _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async ValueTask OnStreamTrade(FinnhubStreamTrade trade,
		CancellationToken cancellationToken)
	{
		if (trade?.Symbol.IsEmpty() != false || trade.Price == null || trade.Timestamp is not > 0)
			return;

		LiveSubscription[] subscriptions;
		var finished = new HashSet<long>();
		var unsubscribe = false;
		lock (_liveSync)
		{
			subscriptions = _liveSubscriptions.Values
				.Where(subscription => subscription.Symbol.EqualsIgnoreCase(trade.Symbol))
				.ToArray();
			foreach (var subscription in subscriptions)
			{
				if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
				{
					_liveSubscriptions.Remove(subscription.TransactionId);
					finished.Add(subscription.TransactionId);
				}
			}
			unsubscribe = finished.Count > 0 && !_liveSubscriptions.Values
				.Any(subscription => subscription.Symbol.EqualsIgnoreCase(trade.Symbol));
		}

		var serverTime = Extensions.FromUnixMilliseconds(trade.Timestamp.Value);
		foreach (var subscription in subscriptions)
		{
			if (subscription.DataType == DataType.Level1)
			{
				await SendOutMessageAsync(new Level1ChangeMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					ServerTime = serverTime,
				}
				.TryAdd(Level1Fields.LastTradePrice, trade.Price)
				.TryAdd(Level1Fields.LastTradeVolume, trade.Volume is > 0 ? trade.Volume : null)
				.TryAdd(Level1Fields.LastTradeTime, serverTime), cancellationToken);
			}
			else
			{
				await SendOutMessageAsync(new ExecutionMessage
				{
					OriginalTransactionId = subscription.TransactionId,
					SecurityId = subscription.SecurityId,
					DataTypeEx = DataType.Ticks,
					ServerTime = serverTime,
					TradePrice = trade.Price,
					TradeVolume = trade.Volume is > 0 ? trade.Volume : null,
				}, cancellationToken);
			}

			if (finished.Contains(subscription.TransactionId))
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		}

		if (unsubscribe)
			await SafeStream().Unsubscribe(trade.Symbol, cancellationToken);
	}

	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		string symbol, long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Symbol = symbol,
			DataType = mdMsg.DataType2,
			Remaining = remaining,
		};

		var isFirst = false;
		lock (_liveSync)
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
				throw new InvalidOperationException(
					$"Finnhub subscription {mdMsg.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item => item.Symbol.EqualsIgnoreCase(symbol));
			_liveSubscriptions.Add(mdMsg.TransactionId, subscription);
		}

		try
		{
			if (isFirst)
				await SafeStream().Subscribe(symbol, cancellationToken);
		}
		catch
		{
			lock (_liveSync)
				_liveSubscriptions.Remove(mdMsg.TransactionId);
			throw;
		}
	}

	private async Task RemoveLiveSubscription(long transactionId,
		CancellationToken cancellationToken)
	{
		LiveSubscription removed;
		var unsubscribe = false;
		lock (_liveSync)
		{
			if (!_liveSubscriptions.Remove(transactionId, out removed))
				return;
			unsubscribe = !_liveSubscriptions.Values
				.Any(item => item.Symbol.EqualsIgnoreCase(removed.Symbol));
		}
		if (unsubscribe)
			await SafeStream().Unsubscribe(removed.Symbol, cancellationToken);
	}
}
