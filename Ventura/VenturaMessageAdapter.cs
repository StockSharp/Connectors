namespace StockSharp.Ventura;

public partial class VenturaMessageAdapter
{
	private VenturaRestClient _restClient;
	private VenturaMarketDataClient _marketDataClient;
	private VenturaOrderStatusClient _orderStatusClient;
	private string _resolvedPortfolioName;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private int _orderRefreshActive;
	private bool _orderStatusOwnsConnectionState;

	/// <summary>
	/// Initializes a new instance of the <see cref="VenturaMessageAdapter"/>.
	/// </summary>
	public VenturaMessageAdapter(IdGenerator transactionIdGenerator)
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
		["NSE", "BSE", "NFO", "BFO"];

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
			Key,
			ClientId,
			Token)
		{
			Parent = this,
		};

		try
		{
			if (Token.IsEmpty())
			{
				VenturaAuthResult auth;
				if (!RequestToken.IsEmpty())
				{
					auth = await _restClient.ExchangeAccessToken(
						Secret,
						RequestToken,
						cancellationToken);
				}
				else
				{
					auth = await _restClient.LoginWithTotp(
						ClientId,
						Secret,
						Pin,
						TotpSecret,
						MacAddress,
						DateTime.UtcNow,
						cancellationToken);
				}
				ClientId = auth.ClientId;
				Token = auth.AuthToken.Secure();
				RefreshToken = auth.RefreshToken?.Secure();
			}
			else
			{
				ClientId.ThrowIfEmpty(nameof(ClientId));
			}

			await _restClient.GetProfile(cancellationToken);
			_resolvedPortfolioName = PortfolioName
				.IsEmpty(ClientId)
				.IsEmpty("Ventura");

			if (this.IsMarketData())
			{
				_marketDataClient = new(
					MarketDataAddress,
					Key,
					ClientId,
					Token,
					ReconnectAttempts,
					ReConnectionSettings.WorkingTime)
				{
					Parent = this,
				};
				_marketDataClient.MarketDataReceived +=
					OnMarketDataReceived;
				_marketDataClient.Error += SendOutErrorAsync;
				_marketDataClient.StateChanged +=
					SendOutConnectionStateAsync;
				await _marketDataClient.Connect(cancellationToken);
			}

			if (this.IsTransactional())
			{
				_orderStatusClient = new(
					OrderStatusAddress,
					Key,
					ClientId,
					Token,
					ReconnectAttempts,
					ReConnectionSettings.WorkingTime)
				{
					Parent = this,
				};
				_orderStatusClient.OrderStatusReceived +=
					OnOrderStatusReceived;
				_orderStatusClient.Error += SendOutErrorAsync;
				_orderStatusOwnsConnectionState =
					_marketDataClient == null;
				if (_orderStatusOwnsConnectionState)
				{
					_orderStatusClient.StateChanged +=
						SendOutConnectionStateAsync;
				}
				await _orderStatusClient.Connect(cancellationToken);
			}

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
			if (_marketDataClient != null)
				await _marketDataClient.Disconnect(cancellationToken);
			if (_orderStatusClient != null)
				await _orderStatusClient.Disconnect(cancellationToken);
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
		if (_orderStatusSubscriptionId != 0 &&
			CurrentTime - _lastOrderRefresh >= PollingInterval)
		{
			await RefreshOrders(
				_orderStatusSubscriptionId,
				cancellationToken);
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
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		_orderRefreshActive = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask OnOrderStatusReceived(
		VenturaOrderStatusUpdate update,
		CancellationToken cancellationToken)
	{
		if (update == null || _orderStatusSubscriptionId == 0)
			return;
		await RefreshOrders(
			_orderStatusSubscriptionId,
			cancellationToken);
	}

	private async ValueTask RefreshOrders(
		long originalTransactionId,
		CancellationToken cancellationToken)
	{
		if (Interlocked.Exchange(ref _orderRefreshActive, 1) != 0)
			return;
		try
		{
			await SendOrderSnapshot(
				originalTransactionId,
				false,
				cancellationToken);
			_lastOrderRefresh = CurrentTime;
		}
		finally
		{
			Volatile.Write(ref _orderRefreshActive, 0);
		}
	}

	private async ValueTask DisposeClients(
		CancellationToken cancellationToken)
	{
		if (_marketDataClient != null)
		{
			_marketDataClient.MarketDataReceived -=
				OnMarketDataReceived;
			_marketDataClient.Error -= SendOutErrorAsync;
			_marketDataClient.StateChanged -=
				SendOutConnectionStateAsync;
			try
			{
				await _marketDataClient.Disconnect(cancellationToken);
			}
			catch (Exception error) when (
				error is OperationCanceledException or IOException or
					WebSocketException)
			{
				this.AddVerboseLog(
					"Ventura market stream cleanup: {0}",
					error.Message);
			}
			_marketDataClient.Dispose();
			_marketDataClient = null;
		}

		if (_orderStatusClient != null)
		{
			_orderStatusClient.OrderStatusReceived -=
				OnOrderStatusReceived;
			_orderStatusClient.Error -= SendOutErrorAsync;
			if (_orderStatusOwnsConnectionState)
			{
				_orderStatusClient.StateChanged -=
					SendOutConnectionStateAsync;
			}
			try
			{
				await _orderStatusClient.Disconnect(cancellationToken);
			}
			catch (Exception error) when (
				error is OperationCanceledException or IOException or
					WebSocketException)
			{
				this.AddVerboseLog(
					"Ventura order stream cleanup: {0}",
					error.Message);
			}
			_orderStatusClient.Dispose();
			_orderStatusClient = null;
		}
		_orderStatusOwnsConnectionState = false;

		if (_restClient != null)
		{
			_restClient.Dispose();
			_restClient = null;
		}
	}
}
