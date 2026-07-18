namespace StockSharp.TwelveData;

public partial class TwelveDataMessageAdapter
{
	private sealed class LiveSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public TwelveDataSecurityKey Key { get; init; }
		public long? Remaining { get; set; }
	}

	private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
	private readonly object _liveSync = new();
	private TwelveDataRestClient _rest;
	private TwelveDataWebSocketClient _stream;

	/// <summary>Initializes a new instance.</summary>
	public TwelveDataMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(Extensions.TimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
	[
		Extensions.StockBoard,
		Extensions.EtfBoard,
		Extensions.ForexBoard,
		Extensions.CryptoBoard,
		Extensions.CommodityBoard,
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest != null || _stream != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (Address == null || !Address.IsAbsoluteUri || Address.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException(
				"Twelve Data REST address must be an absolute HTTPS URI.");
		if (WebSocketAddress == null || !WebSocketAddress.IsAbsoluteUri ||
			!WebSocketAddress.Scheme.EqualsIgnoreCase("wss"))
		{
			throw new InvalidOperationException(
				"Twelve Data WebSocket address must be an absolute WSS URI.");
		}

		var token = Token.UnSecure();
		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Address, token, attempts) { Parent = this };
		_stream = new(WebSocketAddress, token, attempts) { Parent = this };
		_stream.PriceReceived += OnStreamPrice;
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
			stream.PriceReceived -= OnStreamPrice;
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

	private TwelveDataRestClient SafeRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private TwelveDataWebSocketClient SafeStream()
		=> _stream ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private async ValueTask OnStreamPrice(TwelveDataStreamMessage price,
		CancellationToken cancellationToken)
	{
		if (price?.Symbol.IsEmpty() != false || price.Timestamp is not > 0 ||
			price.Price is not > 0 && price.Bid is not > 0 && price.Ask is not > 0)
		{
			return;
		}

		LiveSubscription[] subscriptions;
		TwelveDataSecurityKey[] unsubscribe;
		var finished = new HashSet<long>();
		lock (_liveSync)
		{
			subscriptions = _liveSubscriptions.Values
				.Where(subscription => Matches(price, subscription.Key)).ToArray();
			foreach (var subscription in subscriptions)
			{
				if (subscription.Remaining is > 0 && --subscription.Remaining == 0)
				{
					_liveSubscriptions.Remove(subscription.TransactionId);
					finished.Add(subscription.TransactionId);
				}
			}

			unsubscribe = subscriptions.Where(subscription =>
				finished.Contains(subscription.TransactionId) && !_liveSubscriptions.Values.Any(item =>
					item.Key.ToNative().EqualsIgnoreCase(subscription.Key.ToNative())))
				.GroupBy(subscription => subscription.Key.ToNative(),
					StringComparer.OrdinalIgnoreCase)
				.Select(group => group.First().Key).ToArray();
		}

		var serverTime = Extensions.FromUnixSeconds(price.Timestamp.Value);
		foreach (var subscription in subscriptions)
		{
			await SendOutMessageAsync(new Level1ChangeMessage
			{
				OriginalTransactionId = subscription.TransactionId,
				SecurityId = subscription.SecurityId,
				ServerTime = serverTime,
			}
			.TryAdd(Level1Fields.LastTradePrice, Positive(price.Price))
			.TryAdd(Level1Fields.LastTradeTime, Positive(price.Price) != null ? serverTime : null)
			.TryAdd(Level1Fields.Volume, Positive(price.DayVolume))
			.TryAdd(Level1Fields.BestBidPrice, Positive(price.Bid))
			.TryAdd(Level1Fields.BestBidTime, Positive(price.Bid) != null ? serverTime : null)
			.TryAdd(Level1Fields.BestAskPrice, Positive(price.Ask))
			.TryAdd(Level1Fields.BestAskTime, Positive(price.Ask) != null ? serverTime : null),
				cancellationToken);

			if (finished.Contains(subscription.TransactionId))
				await SendSubscriptionFinishedAsync(subscription.TransactionId, cancellationToken);
		}

		foreach (var key in unsubscribe)
			await SafeStream().Unsubscribe(key, cancellationToken);
	}

	private static bool Matches(TwelveDataStreamMessage price, TwelveDataSecurityKey key)
		=> price.Symbol.EqualsIgnoreCase(key.Symbol) &&
			(key.Exchange.IsEmpty() || price.Exchange.IsEmpty() ||
				price.Exchange.EqualsIgnoreCase(key.Exchange));

	private async Task AddLiveSubscription(MarketDataMessage mdMsg, SecurityId securityId,
		TwelveDataSecurityKey key, long? remaining, CancellationToken cancellationToken)
	{
		var subscription = new LiveSubscription
		{
			TransactionId = mdMsg.TransactionId,
			SecurityId = securityId,
			Key = key,
			Remaining = remaining,
		};

		var native = key.ToNative();
		var isFirst = false;
		lock (_liveSync)
		{
			if (_liveSubscriptions.ContainsKey(mdMsg.TransactionId))
				throw new InvalidOperationException(
					$"Twelve Data subscription {mdMsg.TransactionId} already exists.");
			isFirst = !_liveSubscriptions.Values.Any(item =>
				item.Key.ToNative().EqualsIgnoreCase(native));
			_liveSubscriptions.Add(mdMsg.TransactionId, subscription);
		}

		try
		{
			if (isFirst)
				await SafeStream().Subscribe(key, cancellationToken);
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
			unsubscribe = !_liveSubscriptions.Values.Any(item =>
				item.Key.ToNative().EqualsIgnoreCase(removed.Key.ToNative()));
		}
		if (unsubscribe)
			await SafeStream().Unsubscribe(removed.Key, cancellationToken);
	}

	private static decimal? Positive(decimal? value)
		=> value is > 0 ? value : null;
}
