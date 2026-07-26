namespace StockSharp.Poloniex;

partial class PoloniexMessageAdapter
{
	private PoloniexRestClient _restClient;
	private PoloniexSocketClient _publicSocket;
	private PoloniexSocketClient _privateSocket;
	private Authenticator _authenticator;

	/// <summary>
	/// Initializes a new instance of the <see cref="PoloniexMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public PoloniexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(15);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();

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
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Poloniex];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		_ = connectMsg;

		if (_restClient is not null || _publicSocket is not null || _privateSocket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (this.IsTransactional())
		{
			if (Key.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
			if (Secret.IsEmpty())
				throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
		}

		ClearState();
		_authenticator = new(this.IsTransactional(), Key, Secret);
		_restClient = new(RestEndpoint, _authenticator) { Parent = this };

		await SendOutConnectionStateAsync(ConnectionStates.Connecting, cancellationToken);

		try
		{
			if (_authenticator.CanSign)
			{
				_privateSocket = CreatePrivateSocket();
				await _privateSocket.ConnectAsync(cancellationToken);
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

		if (_privateSocket is not null)
			await _privateSocket.PingAsync(cancellationToken);
	}

	private PoloniexSocketClient CreatePublicSocket()
	{
		var client = new PoloniexSocketClient(WebSocketEndpoint, false, _authenticator,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};

		client.TickerChanged += SessionOnTickerChanged;
		client.BookChanged += SessionOnBookChanged;
		client.NewTrade += SessionOnNewTrade;
		client.Error += SendOutErrorAsync;
		client.StateChanged += OnPublicSocketStateAsync;
		return client;
	}

	private PoloniexSocketClient CreatePrivateSocket()
	{
		var client = new PoloniexSocketClient(PrivateWebSocketEndpoint, true, _authenticator,
			ReConnectionSettings.WorkingTime, ReConnectionSettings.ReAttemptCount)
		{
			Parent = this,
		};

		client.BalanceChanged += SessionOnBalanceChanged;
		client.OrderChanged += SessionOnOrderChanged;
		client.Error += SendOutErrorAsync;
		client.StateChanged += OnPrivateSocketStateAsync;
		return client;
	}

	private async ValueTask OnPublicSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state is ConnectionStates.Restored or ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask OnPrivateSocketStateAsync(ConnectionStates state,
		CancellationToken cancellationToken)
	{
		if (state == ConnectionStates.Failed)
			await SendOutConnectionStateAsync(state, cancellationToken);
	}

	private async ValueTask DisposeClientsAsync(CancellationToken cancellationToken)
	{
		var privateSocket = _privateSocket;
		var publicSocket = _publicSocket;
		_privateSocket = null;
		_publicSocket = null;

		foreach (var client in new[] { privateSocket, publicSocket }
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

		_authenticator?.Dispose();
		_authenticator = null;
	}

	private void ClearState()
	{
		_level1Counter = 0;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_wsBookSubscriptions.Clear();
		_wsTradesSubscriptions.Clear();
	}
}
