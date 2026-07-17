namespace StockSharp.Flattrade;

public partial class FlattradeMessageAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromMinutes(60),
		TimeSpan.FromMinutes(120),
		TimeSpan.FromMinutes(240),
		TimeSpan.FromDays(1),
	];

	private FlattradeRestClient _restClient;
	private FlattradeSocketClient _socketClient;
	private string _resolvedAccountId;
	private DateTime _lastHeartbeat;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="FlattradeMessageAdapter"/>.</summary>
	public FlattradeMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "NFO", "BFO", "CDS", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_restClient != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (ReconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts), ReconnectAttempts, "Reconnect attempts cannot be negative.");

		UserId.ThrowIfEmpty(nameof(UserId));
		Token.ThrowIfEmpty(nameof(Token));
		_resolvedAccountId = AccountId.IsEmpty() ? UserId : AccountId;
		_restClient = new(UserId, _resolvedAccountId, Token) { Parent = this };

		try
		{
			if (this.IsTransactional())
				await _restClient.GetLimits(cancellationToken);

			if (this.IsMarketData() || this.IsTransactional())
			{
				_socketClient = new(UserId, _resolvedAccountId, Token, this.IsTransactional(),
					ReconnectAttempts, ReConnectionSettings.WorkingTime) { Parent = this };
				_socketClient.MarketDataReceived += OnMarketDataReceived;
				_socketClient.OrderReceived += OnOrderReceived;
				_socketClient.PositionReceived += OnPositionReceived;
				_socketClient.Error += SendOutErrorAsync;
				_socketClient.StateChanged += SendOutConnectionStateAsync;
				await _socketClient.Connect(cancellationToken);
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

		if (_socketClient != null)
			await _socketClient.Disconnect(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_socketClient != null && CurrentTime - _lastHeartbeat >= TimeSpan.FromSeconds(20))
		{
			await _socketClient.SendHeartbeat(cancellationToken);
			_lastHeartbeat = CurrentTime;
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
		_marketStates.Clear();
		_lastTicks.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_resolvedAccountId = null;
		_lastHeartbeat = default;
		_lastPortfolioRefresh = default;

		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask DisposeClients(CancellationToken cancellationToken)
	{
		if (_socketClient != null)
		{
			_socketClient.MarketDataReceived -= OnMarketDataReceived;
			_socketClient.OrderReceived -= OnOrderReceived;
			_socketClient.PositionReceived -= OnPositionReceived;
			_socketClient.Error -= SendOutErrorAsync;
			_socketClient.StateChanged -= SendOutConnectionStateAsync;
			_socketClient.Dispose();
			_socketClient = null;
		}

		_restClient?.Dispose();
		_restClient = null;
		await Task.CompletedTask;
	}
}
