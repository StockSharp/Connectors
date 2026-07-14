namespace StockSharp.LMAX;

partial class LmaxMessageAdapter
{
	private Authenticator _authenticator;
	private HttpClient _httpClient;
	private SocketClient _socketClient;

	/// <summary>
	/// Initializes a new instance of the <see cref="LmaxMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public LmaxMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = DefaultHeartbeatInterval;

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Lmax];

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		try
		{
			if (_socketClient != null)
			{
				_socketClient.StateChanged -= SendOutConnectionStateAsync;
				_socketClient.Error -= SendOutErrorAsync;
				_socketClient.OrderBookReceived -= OnOrderBookReceived;
				_socketClient.TickerReceived -= OnTickerReceived;
				_socketClient.TradeReceived -= OnTradeReceived;
				_socketClient.OrderReceived -= OnOrderReceived;
				_socketClient.ExecutionReceived -= OnExecutionReceived;
				_socketClient.PositionReceived -= OnPositionReceived;
				_socketClient.WalletReceived -= OnWalletReceived;
				_socketClient.RejectionReceived -= OnRejectionReceived;

				_socketClient.Dispose();
				_socketClient = null;
			}

			_httpClient?.Dispose();
			_httpClient = null;
			_authenticator = null;
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, cancellationToken);
		}

		_instrumentIds.Clear();

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if (Key.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.LoginNotSpecified);

		if (Secret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.PasswordNotSpecified);

		_authenticator = new Authenticator(Key, Secret);

		var (accountApiUrl, marketDataApiUrl, marketDataWsUrl, accountWsUrl) = GetUrls();

		_httpClient = new(_authenticator, accountApiUrl, marketDataApiUrl)
		{
			Parent = this
		};

		await FillSecIds(cancellationToken);

		var token = (await _httpClient.ConnectAsync(cancellationToken)).Secure();

		_socketClient = new(marketDataWsUrl, accountWsUrl, () => token, ReConnectionSettings.WorkingTime)
		{
			Parent = this
		};

		_socketClient.StateChanged += SendOutConnectionStateAsync;
		_socketClient.Error += SendOutErrorAsync;
		_socketClient.OrderBookReceived += OnOrderBookReceived;
		_socketClient.TickerReceived += OnTickerReceived;
		_socketClient.TradeReceived += OnTradeReceived;
		_socketClient.OrderReceived += OnOrderReceived;
		_socketClient.ExecutionReceived += OnExecutionReceived;
		_socketClient.PositionReceived += OnPositionReceived;
		_socketClient.WalletReceived += OnWalletReceived;
		_socketClient.RejectionReceived += OnRejectionReceived;

		await _socketClient.ConnectAsync(cancellationToken);

		await SendOutMessageAsync(new ConnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_httpClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		_socketClient?.Disconnect();
		_httpClient.Disconnect();

		return base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		await _httpClient.GetServerTimeAsync(cancellationToken);
	}

	private (string accountApi, string marketDataApi, string marketDataWs, string accountWs) GetUrls()
	{
		if (IsDemo)
		{
			return (
				"https://account-api.london-demo.lmax.com",
				"https://market-data-api.london-demo.lmax.com",
				"wss://market-data-api.london-demo.lmax.com/v1/web-socket",
				"wss://account-api.london-demo.lmax.com/v1/web-socket"
			);
		}

		return Location switch
		{
			LmaxLocations.London => (
				"https://account-api.london-professional.lmax.com",
				"https://market-data-api.london-professional.lmax.com",
				"wss://market-data-api.london-professional.lmax.com/v1/web-socket",
				"wss://account-api.london-professional.lmax.com/v1/web-socket"
			),
			LmaxLocations.NewYork => (
				"https://account-api.newyork-professional.lmax.com",
				"https://market-data-api.newyork-professional.lmax.com",
				"wss://market-data-api.newyork-professional.lmax.com/v1/web-socket",
				"wss://account-api.newyork-professional.lmax.com/v1/web-socket"
			),
			LmaxLocations.Tokyo => (
				"https://account-api.tokyo-professional.lmax.com",
				"https://market-data-api.tokyo-professional.lmax.com",
				"wss://market-data-api.tokyo-professional.lmax.com/v1/web-socket",
				"wss://account-api.tokyo-professional.lmax.com/v1/web-socket"
			),
			LmaxLocations.Singapore => (
				"https://account-api.singapore-professional.lmax.com",
				"https://market-data-api.singapore-professional.lmax.com",
				"wss://market-data-api.singapore-professional.lmax.com/v1/web-socket",
				"wss://account-api.singapore-professional.lmax.com/v1/web-socket"
			),
			LmaxLocations.Digital => (
				"https://account-api.london-digital.lmax.com",
				"https://market-data-api.london-digital.lmax.com",
				"wss://market-data-api.london-digital.lmax.com/v1/web-socket",
				"wss://account-api.london-digital.lmax.com/v1/web-socket"
			),
			_ => throw new ArgumentOutOfRangeException(nameof(Location), Location, LocalizedStrings.InvalidValue)
		};
	}

}
