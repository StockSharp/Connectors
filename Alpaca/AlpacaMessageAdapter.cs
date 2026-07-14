namespace StockSharp.Alpaca;

partial class AlpacaMessageAdapter
{
	private RestTradingClient _tradingClient;
	private RestStockClient _stockClient;
	private RestCryptoClient _cryptoClient;
	private RestNewsClient _newsClient;

	private SocketTradingClient _socketTradingClient;
	private SocketStockClient _socketStockClient;
	private SocketCryptoClient _socketCryptoClient;
	private SocketNewsClient _socketNewsClient;

	private ConnectionStateTracker _tracker;

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

		_tradingClient = new(IsDemo, Key, Secret) { Parent = this };
		_stockClient = new(Key, Secret) { Parent = this };
		_cryptoClient = new(Key, Secret) { Parent = this };
		_newsClient = new(Key, Secret) { Parent = this };

		var attemptsCount = ReConnectionSettings.ReAttemptCount;

		_socketTradingClient = new(IsDemo, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketStockClient = new(IsDemo, StockFeed, Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketCryptoClient = new(Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };
		_socketNewsClient = new(Key, Secret, attemptsCount, ReConnectionSettings.WorkingTime) { Parent = this };

		SubscribeSocketClient(_socketTradingClient);
		SubscribeSocketClient(_socketStockClient);
		SubscribeSocketClient(_socketCryptoClient);
		SubscribeSocketClient(_socketNewsClient);

		var isTrans = this.IsTransactional();
		var isMD = this.IsMarketData();

		if (!isTrans && !isMD)
		{
			await base.ConnectAsync(msg, cancellationToken);
			return;
		}

		_tracker = new();
		_tracker.StateChanged += SendOutConnectionStateAsync;

		if (isTrans)
			_tracker.Add(_socketTradingClient);

		if (isMD)
		{
			if (Sections.Contains(AlpacaSections.Stock))
				_tracker.Add(_socketStockClient);

			if (Sections.Contains(AlpacaSections.Crypto))
				_tracker.Add(_socketCryptoClient);

			_tracker.Add(_socketNewsClient);
		}

		await _tracker.ConnectAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage msg, CancellationToken cancellationToken)
	{
		if (_tradingClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_socketTradingClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_tracker is null)
			return base.DisconnectAsync(msg, cancellationToken);
		
		_tracker.Disconnect();
		return default;
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		_cryptoSecIds.Clear();
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
		_newsClient = await disposeClient(_newsClient);
		_tradingClient = await disposeClient(_tradingClient);

		ValueTask<T> disposeSocketClient<T>(T client, Action<T> unsubscribe)
			where T : SocketAlpacaClient
		{
			if (client is not null)
				unsubscribe(client);

			return disposeClient(client);
		}

		_socketStockClient = await disposeSocketClient(_socketStockClient, UnsubscribeSocketClient);
		_socketCryptoClient = await disposeSocketClient(_socketCryptoClient, UnsubscribeSocketClient);
		_socketNewsClient = await disposeSocketClient(_socketNewsClient, UnsubscribeSocketClient);
		_socketTradingClient = await disposeSocketClient(_socketTradingClient, UnsubscribeSocketClient);

		if (_tracker is not null)
		{
			_tracker.StateChanged -= SendOutConnectionStateAsync;
			_tracker.Dispose();
			_tracker = null;
		}

		await SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}
}