namespace StockSharp.MotilalOswal;

public partial class MotilalOswalMessageAdapter
{
	private MotilalOswalRestClient _restClient;
	private MotilalOswalMarketDataClient _marketClient;
	private MotilalOswalOrderClient _orderClient;
	private DateTime _lastOrderHeartbeat;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Initializes a new instance of the <see cref="MotilalOswalMessageAdapter"/>.</summary>
	public MotilalOswalMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);

		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "NFO", "CDS", "MCX", "NCDEX", "BFO", "BCD"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (ReconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts), ReconnectAttempts, "Reconnect attempts cannot be negative.");

		_restClient = new(IsDemo, Key, Secret, Token, AccessToken, ClientCode,
			LocalIp, PublicIp, MacAddress, VendorInfo, InstalledAppId) { Parent = this };
		await _restClient.GetProfile(cancellationToken);

		try
		{
			if (this.IsMarketData())
			{
				var limit = await _restClient.GetBroadcastLimit(cancellationToken);
				_marketClient = new(ClientCode, limit, ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
				_marketClient.UpdateReceived += OnMarketUpdate;
				_marketClient.Error += SendOutErrorAsync;
				_marketClient.StateChanged += SendOutConnectionStateAsync;
				await _marketClient.Connect(cancellationToken);
			}

			if (this.IsTransactional())
			{
				_orderClient = new(IsDemo, ClientCode, Token, Key, ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
				_orderClient.OrderReceived += OnOrderUpdate;
				_orderClient.TradeReceived += OnTradeUpdate;
				_orderClient.Error += SendOutErrorAsync;
				if (_marketClient == null)
					_orderClient.StateChanged += SendOutConnectionStateAsync;
				await _orderClient.Connect(cancellationToken);
			}

			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients(cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_orderClient != null)
			await _orderClient.Disconnect(cancellationToken);
		if (_marketClient != null)
			await _marketClient.Disconnect(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_orderClient != null && CurrentTime - _lastOrderHeartbeat >= TimeSpan.FromSeconds(25))
		{
			await _orderClient.SendHeartbeat(cancellationToken);
			_lastOrderHeartbeat = CurrentTime;
		}

		if (_portfolioSubscriptionId != 0 && CurrentTime - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
			_lastPortfolioRefresh = CurrentTime;
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await DisposeClients(cancellationToken);

		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_depthBooks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastOrderHeartbeat = default;
		_lastPortfolioRefresh = default;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask DisposeClients(CancellationToken cancellationToken)
	{
		if (_marketClient != null)
		{
			_marketClient.UpdateReceived -= OnMarketUpdate;
			_marketClient.Error -= SendOutErrorAsync;
			_marketClient.StateChanged -= SendOutConnectionStateAsync;
			_marketClient.Dispose();
			_marketClient = null;
		}

		if (_orderClient != null)
		{
			_orderClient.OrderReceived -= OnOrderUpdate;
			_orderClient.TradeReceived -= OnTradeUpdate;
			_orderClient.Error -= SendOutErrorAsync;
			_orderClient.StateChanged -= SendOutConnectionStateAsync;
			_orderClient.Dispose();
			_orderClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		await Task.CompletedTask;
	}
}
