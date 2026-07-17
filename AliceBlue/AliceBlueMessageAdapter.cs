namespace StockSharp.AliceBlue;

public partial class AliceBlueMessageAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromDays(1),
	];

	private AliceBlueRestClient _restClient;
	private AliceBlueMarketClient _marketClient;
	private AliceBlueOrderClient _orderClient;
	private string _resolvedClientId;
	private bool _isMarketSessionCreated;
	private DateTime _lastMarketHeartbeat;
	private DateTime _lastOrderHeartbeat;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="AliceBlueMessageAdapter"/>.</summary>
	public AliceBlueMessageAdapter(IdGenerator transactionIdGenerator)
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
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "NFO", "BFO", "CDS", "BCD", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (ReconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts), ReconnectAttempts,
				"Reconnect attempts cannot be negative.");

		UserId.ThrowIfEmpty(nameof(UserId));
		Token.ThrowIfEmpty(nameof(Token));
		_restClient = new(UserId, Token) { Parent = this };

		try
		{
			var profile = await _restClient.GetProfile(cancellationToken);
			_resolvedClientId = ClientId.IsEmpty(profile.ClientId).IsEmpty(UserId);

			if (this.IsMarketData())
			{
				await _restClient.CreateMarketSession(cancellationToken);
				_isMarketSessionCreated = true;
				_marketClient = new(_resolvedClientId, Token, ReconnectAttempts,
					ReConnectionSettings.WorkingTime) { Parent = this };
				_marketClient.MarketDataReceived += OnMarketDataReceived;
				_marketClient.Error += SendOutErrorAsync;
				_marketClient.StateChanged += SendOutConnectionStateAsync;
				await _marketClient.Connect(cancellationToken);
			}

			if (this.IsTransactional())
			{
				var orderToken = await _restClient.GetOrderToken(cancellationToken);
				_orderClient = new(UserId, orderToken, ReconnectAttempts,
					ReConnectionSettings.WorkingTime) { Parent = this };
				_orderClient.OrderReceived += OnOrderReceived;
				_orderClient.Error += SendOutErrorAsync;
				if (_marketClient == null)
					_orderClient.StateChanged += SendOutConnectionStateAsync;
				await _orderClient.Connect(cancellationToken);
			}

			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients(cancellationToken, true);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_restClient == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		if (_orderClient != null)
			await _orderClient.Disconnect(cancellationToken);
		if (_marketClient != null)
			await _marketClient.Disconnect(cancellationToken);
		if (_isMarketSessionCreated)
		{
			await _restClient.InvalidateMarketSession(cancellationToken);
			_isMarketSessionCreated = false;
		}
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_marketClient != null && CurrentTime - _lastMarketHeartbeat >= TimeSpan.FromSeconds(40))
		{
			await _marketClient.SendHeartbeat(cancellationToken);
			_lastMarketHeartbeat = CurrentTime;
		}

		if (_orderClient != null && CurrentTime - _lastOrderHeartbeat >= TimeSpan.FromSeconds(50))
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
		await DisposeClients(cancellationToken, true);

		_marketSubscriptions.Clear();
		_securityIds.Clear();
		_instruments.Clear();
		_marketStates.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_resolvedClientId = null;
		_isMarketSessionCreated = false;
		_lastMarketHeartbeat = default;
		_lastOrderHeartbeat = default;
		_lastPortfolioRefresh = default;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask DisposeClients(CancellationToken cancellationToken, bool invalidateSession)
	{
		if (_orderClient != null)
		{
			_orderClient.OrderReceived -= OnOrderReceived;
			_orderClient.Error -= SendOutErrorAsync;
			_orderClient.StateChanged -= SendOutConnectionStateAsync;
			_orderClient.Dispose();
			_orderClient = null;
		}

		if (_marketClient != null)
		{
			_marketClient.MarketDataReceived -= OnMarketDataReceived;
			_marketClient.Error -= SendOutErrorAsync;
			_marketClient.StateChanged -= SendOutConnectionStateAsync;
			_marketClient.Dispose();
			_marketClient = null;
		}

		if (invalidateSession && _isMarketSessionCreated && _restClient != null)
		{
			try
			{
				await _restClient.InvalidateMarketSession(cancellationToken);
			}
			catch (Exception error)
			{
				this.AddWarningLog("Alice Blue market WebSocket session cleanup failed: {0}", error.Message);
			}
		}

		_isMarketSessionCreated = false;
		_restClient?.Dispose();
		_restClient = null;
	}
}
