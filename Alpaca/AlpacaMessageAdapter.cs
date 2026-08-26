namespace StockSharp.Alpaca;

partial class AlpacaMessageAdapter
{
	private RestTradingClient _tradingClient;
	private RestStockClient _stockClient;
	private RestCryptoClient _cryptoClient;
	private RestOptionClient _optionClient;
	private RestNewsClient _newsClient;

	private SocketTradingClient _socketTradingClient;
	private SocketStockClient _socketStockClient;
	private SocketCryptoClient _socketCryptoClient;
	private SocketNewsClient _socketNewsClient;

	private ConnectionStateTracker _tracker;

	private readonly SynchronizedSet<SocketAlpacaClient> _openStreams = [];
	private readonly SemaphoreSlim _streamOpening = new(1, 1);

	/// <summary>
	/// Initializes a new instance of the <see cref="AlpacaMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public AlpacaMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.News);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_streamOpening.Dispose();

		base.DisposeManaged();
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Level1 || dataType == DataType.Ticks || dataType.IsTFCandles || dataType == DataType.Transactions || dataType == DataType.PositionChanges;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <summary>
	/// All possible time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => AlpacaExtensions.TimeFrames.Keys;

	private void SubscribeSocketClient(SocketTradingClient client)
	{
		if (client is null)
			throw new ArgumentNullException(nameof(client));

		client.Error += SendOutErrorAsync;
		client.OrderReceived += OnOrderReceived;
	}

	private void UnsubscribeSocketClient(SocketTradingClient client)
	{
		client.Error -= SendOutErrorAsync;
		client.OrderReceived -= OnOrderReceived;
	}

	private void SubscribeSocketClient(SocketMarketDataClient client)
	{
		if (client is null)
			throw new ArgumentNullException(nameof(client));

		client.Error += SendOutErrorAsync;
		client.OhlcReceived += OnOhlcReceived;
		client.TickReceived += OnTickReceived;
		client.QuoteReceived += OnQuoteReceived;
		client.OrderBookReceived += OnOrderBookReceived;
		client.NewsReceived += OnNewsReceived;
	}

	private void UnsubscribeSocketClient(SocketMarketDataClient client)
	{
		client.Error -= SendOutErrorAsync;
		client.OhlcReceived -= OnOhlcReceived;
		client.TickReceived -= OnTickReceived;
		client.QuoteReceived -= OnQuoteReceived;
		client.OrderBookReceived -= OnOrderBookReceived;
		client.NewsReceived -= OnNewsReceived;
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage msg, CancellationToken cancellationToken)
	{
		if (Key.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

		if (Secret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);

		if (_tradingClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (_socketTradingClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		var tradingRestEndpoint = IsDemo ? DemoTradingRestEndpoint : TradingRestEndpoint;
		var tradingWebSocketEndpoint = IsDemo ? DemoTradingWebSocketEndpoint : TradingWebSocketEndpoint;
		_tradingClient = new(tradingRestEndpoint, Key, Secret) { Parent = this };
		_stockClient = new(MarketDataRestEndpoint, Key, Secret) { Parent = this };
		_cryptoClient = new(MarketDataRestEndpoint, Key, Secret) { Parent = this };
		_optionClient = new(MarketDataRestEndpoint, Key, Secret) { Parent = this };
		_newsClient = new(MarketDataRestEndpoint, Key, Secret) { Parent = this };

		var attemptsCount = ReConnectionSettings.ReAttemptCount;

		_socketTradingClient = new(tradingWebSocketEndpoint, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketStockClient = new(MarketDataWebSocketEndpoint, StockFeed, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketCryptoClient = new(MarketDataWebSocketEndpoint, CryptoLocation, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketNewsClient = new(MarketDataWebSocketEndpoint, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribeSocketClient(_socketTradingClient);
		SubscribeSocketClient(_socketStockClient);
		SubscribeSocketClient(_socketCryptoClient);
		SubscribeSocketClient(_socketNewsClient);

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		// Orders report over their stream, so a transactional session is not usable until it is up.
		// Market data streams are opened by whoever needs one: history comes over REST, and the venue
		// sells its streams apart from it, so an account can hold years of bars and no stream at all.
		if (!this.IsTransactional())
		{
			await base.ConnectAsync(msg, cancellationToken);
			return;
		}

		await OpenStreamAsync(_socketTradingClient, cancellationToken);
	}

	/// <summary>
	/// How many live streams are open.
	/// </summary>
	internal int OpenStreamsCount => _openStreams.Count;

	private bool IsStreamOpen(SocketAlpacaClient client) => _openStreams.Contains(client);

	/// <summary>
	/// Opens a live stream unless it is open already.
	/// </summary>
	/// <param name="client">The stream to open.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <remarks>
	/// The tracker aggregates the state of every stream it holds, so a stream joins it only once it is
	/// wanted: a stream that is never opened would otherwise hold the whole connection short of ready.
	/// </remarks>
	private async ValueTask OpenStreamAsync(SocketAlpacaClient client, CancellationToken cancellationToken)
	{
		if (client is null)
			throw new ArgumentNullException(nameof(client));

		if (IsStreamOpen(client))
			return;

		await _streamOpening.WaitAsync(cancellationToken);

		try
		{
			if (IsStreamOpen(client))
				return;

			_tracker.Add(client);

			try
			{
				await client.ConnectAsync(cancellationToken);
			}
			catch
			{
				_tracker.Remove(client);
				throw;
			}

			_openStreams.Add(client);
		}
		finally
		{
			_streamOpening.Release();
		}
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_tradingClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_socketTradingClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_tracker is null || _openStreams.Count == 0)
			return base.DisconnectAsync(msg, cancellationToken);

		return _tracker.DisconnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_cryptoSecIds.Clear();
		_optionSecIds.Clear();
		_assetIds.Clear();
		_mdTransIds.Clear();
		_accountName = default;

		async ValueTask<T> disposeClient<T>(T client)
			where T : class, IDisposable
		{
			try
			{
				client?.Dispose();
			}
			catch (Exception ex)
			{
				await SendOutErrorAsync(ex, cancellationToken);
			}

			return null;
		}

		_stockClient = await disposeClient(_stockClient);
		_cryptoClient = await disposeClient(_cryptoClient);
		_optionClient = await disposeClient(_optionClient);
		_newsClient = await disposeClient(_newsClient);
		_tradingClient = await disposeClient(_tradingClient);

		async ValueTask<T> disposeSocketClient<T>(T client, Action<T> unsubscribe)
			where T : SocketAlpacaClient
		{
			if (client is not null)
			{
				unsubscribe(client);

				try
				{
					// mark the disconnect as expected, otherwise disposing the socket looks like a connection loss
					await client.DisconnectAsync(cancellationToken);
				}
				catch (Exception ex)
				{
					await SendOutErrorAsync(ex, cancellationToken);
				}
			}

			return await disposeClient(client);
		}

		_socketStockClient = await disposeSocketClient(_socketStockClient, UnsubscribeSocketClient);
		_socketCryptoClient = await disposeSocketClient(_socketCryptoClient, UnsubscribeSocketClient);
		_socketNewsClient = await disposeSocketClient(_socketNewsClient, UnsubscribeSocketClient);
		_socketTradingClient = await disposeSocketClient(_socketTradingClient, UnsubscribeSocketClient);

		_openStreams.Clear();

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}
}
