namespace StockSharp.HitBtc;

partial class HitBtcMessageAdapter
{
	private HitBtcRestClient _restClient;
	private HitBtcSocketClient _publicSocket;
	private HitBtcSocketClient _tradingSocket;

	/// <summary>
	/// Initializes a new instance of the <see cref="HitBtcMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public HitBtcMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.HitBtc];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;

		if (_restClient is not null || _publicSocket is not null || _tradingSocket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		ClearState();
		_restClient = new(RestEndpoint, Key, Secret) { Parent = this };

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);

		try
		{
			if (this.IsTransactional())
			{
				_tradingSocket = CreateTradingSocket();
				await _tradingSocket.ConnectAsync(cancellationToken);
			}

			_publicSocket = CreatePublicSocket();
			await _publicSocket.ConnectAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Connected, cancellationToken);
		}
		catch
		{
			await DisposeClientsAsync(cancellationToken);
			await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		_ = disconnectMsg;

		if (_restClient is null || _publicSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await SendOutConnectionStateAsync(ConnectionStates.Disconnecting, cancellationToken);
		await DisposeClientsAsync(cancellationToken);
		await SendOutConnectionStateAsync(ConnectionStates.Disconnected, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClientsAsync(cancellationToken);
		ClearState();
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		_ = timeMsg;

		if (_publicSocket is not null)
			await _publicSocket.PingAsync(cancellationToken);

		if (_tradingSocket is not null)
			await _tradingSocket.PingAsync(cancellationToken);
	}

	private HitBtcSocketClient CreatePublicSocket()
	{
		var client = new HitBtcSocketClient(WebSocketEndpoint, false, Key, Secret,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};

		client.TickerChanged += SessionOnTickerChanged;
		client.OrderBookChanged += SessionOnOrderBookChanged;
		client.NewTrades += SessionOnNewTrades;
		client.NewCandle += SessionOnNewCandle;
		client.Error += SendOutErrorAsync;
		client.StateChanged += OnPublicSocketStateAsync;
		return client;
	}

	private HitBtcSocketClient CreateTradingSocket()
	{
		var client = new HitBtcSocketClient(TradingWebSocketEndpoint, true, Key, Secret,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};

		client.NewOrders += SessionOnNewOrders;
		client.OrderChanged += SessionOnOrderChanged;
		client.BalanceChanged += SessionOnBalanceChanged;
		client.OrderError += SessionOnOrderError;
		client.Error += SendOutErrorAsync;
		client.StateChanged += OnTradingSocketStateAsync;
		return client;
	}

	private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask OnTradingSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var tradingSocket = _tradingSocket;
		var publicSocket = _publicSocket;
		_tradingSocket = null;
		_publicSocket = null;

		foreach (var client in new[] { tradingSocket, publicSocket }
			.Where(static client => client is not null))
		{
			try
			{
				await client.DisconnectAsync(cancellationToken);
			}
			catch (Exception error) when (!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}

			client.Dispose();
		}

		_restClient?.Dispose();
		_restClient = null;
	}

	private void ClearState()
	{
		_securityIds.Clear();
		_level1Subscriptions.Clear();
		_bookSubscriptions.Clear();
		_tradeSubscriptions.Clear();
		_candleSubscriptions.Clear();
	}
}
