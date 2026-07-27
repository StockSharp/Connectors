namespace StockSharp.Nubra;

public partial class NubraMessageAdapter
{
	private NubraRestClient _restClient;
	private NubraMarketDataClient _marketClient;
	private string _resolvedPortfolioName;
	private DateTime _referenceDate;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>
	/// Initializes a new instance of the <see cref="NubraMessageAdapter"/>.
	/// </summary>
	public NubraMessageAdapter(IdGenerator transactionIdGenerator)
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
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } =
		Enumerable.Range(1, 20).ToArray();

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		["NSE", "BSE", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(
		ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(
				LocalizedStrings.NotDisconnectPrevTime);
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

		DeviceId.ThrowIfEmpty(nameof(DeviceId));
		_referenceDate = DateTime.UtcNow.AddHours(5.5).Date;
		_restClient = new(
			EffectiveRestAddress,
			DeviceId,
			Token)
		{
			Parent = this,
		};

		try
		{
			if (Token.IsEmpty())
			{
				var login = await _restClient.LoginWithTotp(
					Phone,
					Mpin,
					TotpSecret,
					DateTime.UtcNow,
					cancellationToken);
				Token = login.SessionToken.Secure();
			}

			var user = await _restClient.GetUserInfo(cancellationToken);
			_resolvedPortfolioName = PortfolioName
				.IsEmpty(user.ClientCode)
				.IsEmpty(Phone)
				.IsEmpty("Nubra");

			if (this.IsMarketData())
			{
				_marketClient = new(
					EffectiveMarketDataAddress,
					Token,
					ReconnectAttempts,
					ReConnectionSettings.WorkingTime)
				{
					Parent = this,
				};
				_marketClient.MarketDataReceived += OnMarketDataReceived;
				_marketClient.Error += SendOutErrorAsync;
				_marketClient.StateChanged +=
					SendOutConnectionStateAsync;
				await _marketClient.Connect(cancellationToken);
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
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);

		try
		{
			if (_marketClient != null)
				await _marketClient.Disconnect(cancellationToken);
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
		_depths.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_tradeQuantities.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_resolvedPortfolioName = null;
		_referenceDate = default;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask DisposeClients(
		CancellationToken cancellationToken)
	{
		if (_marketClient != null)
		{
			_marketClient.MarketDataReceived -= OnMarketDataReceived;
			_marketClient.Error -= SendOutErrorAsync;
			_marketClient.StateChanged -= SendOutConnectionStateAsync;
			try
			{
				await _marketClient.Disconnect(cancellationToken);
			}
			catch (Exception error) when (
				error is OperationCanceledException or IOException)
			{
				this.AddVerboseLog(
					"Nubra market stream cleanup: {0}",
					error.Message);
			}
			_marketClient.Dispose();
			_marketClient = null;
		}

		if (_restClient != null)
		{
			_restClient.Dispose();
			_restClient = null;
		}
	}
}
