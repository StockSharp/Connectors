namespace StockSharp.HdfcSecurities;

public partial class HdfcMessageAdapter
{
	private HdfcRestClient _restClient;
	private HdfcSocketClient _socketClient;
	private string _resolvedPortfolioName;
	private DateTime _lastHeartbeat;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>
	/// Initializes a new instance of the <see cref="HdfcMessageAdapter"/>.
	/// </summary>
	public HdfcMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
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
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient != null)
		{
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
		}
		if (PollingInterval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(
				nameof(PollingInterval),
				PollingInterval,
				"Polling interval must be positive.");
		}
		if (ReconnectAttempts < 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(ReconnectAttempts),
				ReconnectAttempts,
				"Reconnect attempts cannot be negative.");
		}

		Key.ThrowIfEmpty(nameof(Key));
		_restClient = new(
			RestAddress,
			InstrumentAddress,
			Key,
			Token)
		{
			Parent = this,
		};

		try
		{
			if (Token.IsEmpty())
			{
				var accessToken = await _restClient.ExchangeAccessToken(
					Secret,
					RequestToken,
					cancellationToken);
				Token = accessToken.Secure();
			}

			var profile = await _restClient.GetProfile(cancellationToken);
			if (profile == null || profile.UserId.IsEmpty())
			{
				throw new InvalidDataException(
					"HDFC Securities profile response returned no user ID.");
			}
			_resolvedPortfolioName = PortfolioName
				.IsEmpty(profile.UserId)
				.IsEmpty("HDFC Securities");

			if (this.IsMarketData())
			{
				_socketClient = new(
					WebSocketAddress,
					Key,
					Token,
					ReconnectAttempts,
					ReConnectionSettings.WorkingTime)
				{
					Parent = this,
				};
				_socketClient.MarketDataReceived += OnMarketDataReceived;
				_socketClient.Error += SendOutErrorAsync;
				_socketClient.StateChanged +=
					SendOutConnectionStateAsync;
				await _socketClient.Connect(cancellationToken);
			}

			_lastHeartbeat = CurrentTime;
			_lastOrderRefresh = CurrentTime;
			_lastPortfolioRefresh = CurrentTime;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients(cancellationToken);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient == null)
		{
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
		}

		try
		{
			if (_socketClient != null)
				await _socketClient.Disconnect(cancellationToken);
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			await DisposeClients(cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(
		TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		if (_socketClient != null &&
			CurrentTime - _lastHeartbeat >= TimeSpan.FromSeconds(20))
		{
			await _socketClient.SendHeartbeat(cancellationToken);
			_lastHeartbeat = CurrentTime;
		}

		if (_orderStatusSubscriptionId != 0 &&
			CurrentTime - _lastOrderRefresh >= PollingInterval)
		{
			await SendOrderSnapshot(
				_orderStatusSubscriptionId,
				false,
				cancellationToken);
			_lastOrderRefresh = CurrentTime;
		}

		if (_portfolioSubscriptionId != 0 &&
			CurrentTime - _lastPortfolioRefresh >= PollingInterval)
		{
			await SendPortfolioSnapshot(
				_portfolioSubscriptionId,
				cancellationToken);
			_lastPortfolioRefresh = CurrentTime;
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(
		ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		await DisposeClients(cancellationToken);
		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_resolvedPortfolioName = null;
		_lastHeartbeat = default;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask DisposeClients(
		CancellationToken cancellationToken)
	{
		if (_socketClient != null)
		{
			_socketClient.MarketDataReceived -= OnMarketDataReceived;
			_socketClient.Error -= SendOutErrorAsync;
			_socketClient.StateChanged -= SendOutConnectionStateAsync;
			try
			{
				await _socketClient.Disconnect(cancellationToken);
			}
			catch (Exception error) when (
				error is OperationCanceledException or IOException or
					WebSocketException)
			{
				this.AddVerboseLog(
					"HDFC Securities stream cleanup: {0}",
					error.Message);
			}
			_socketClient.Dispose();
			_socketClient = null;
		}

		if (_restClient != null)
		{
			_restClient.Dispose();
			_restClient = null;
		}
	}
}
