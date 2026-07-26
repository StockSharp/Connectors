namespace StockSharp.Bcs;

public partial class BcsMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public BcsMarketSubscription Native { get; init; }
		public TimeSpan? TimeFrame { get; init; }
	}

	private BcsRestClient _rest;
	private BcsSocketClient _socket;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription>
		_marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _trackedClientOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _seenTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastPoll;
	private DateTime _lastPing;
	private DateTime _lastOrderSearch;
	private string _resolvedPortfolioName;
	private OrderStatusMessage _orderStatusFilter;

	/// <summary>
	/// Initializes a new instance of the <see cref="BcsMessageAdapter"/> class.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public BcsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(BcsExtensions.TimeFrames);
	}

	/// <summary>
	/// Supported candle time frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => BcsExtensions.TimeFrames;

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles ||
			dataType == DataType.Ticks || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest is not null || _socket is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (Token.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.TokenNotSpecified);
		if (PollingInterval < TimeSpan.FromSeconds(1))
			throw new InvalidOperationException(
				"BCS polling interval must be at least one second.");

		_rest = new(RestEndpoint, Token, IsReadOnly,
			Math.Max(1, ReConnectionSettings.ReAttemptCount),
			token => Token = token.Secure())
		{
			Parent = this,
		};

		try
		{
			await _rest.Authenticate(cancellationToken);
			_socket = new(WebSocketEndpoint, async token =>
			{
				await _rest.Authenticate(token);
				return _rest.AccessToken;
			}, Math.Max(1, ReConnectionSettings.ReAttemptCount),
				ReConnectionSettings.WorkingTime)
			{
				Parent = this,
			};
			_socket.QuoteReceived += ProcessQuote;
			_socket.OrderBookReceived += ProcessOrderBook;
			_socket.TradeReceived += ProcessPublicTrade;
			_socket.CandleReceived += ProcessCandle;
			_socket.Error += SendOutErrorAsync;
			_socket.StateChanged += SendOutConnectionStateAsync;

			await _socket.ConnectAsync(cancellationToken);
			_lastPing = _lastPoll = DateTime.UtcNow;
			_resolvedPortfolioName = PortfolioName;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(
		DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_rest is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		try
		{
			if (_socket is not null)
				await _socket.DisconnectAsync(cancellationToken);
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			DisposeClients();
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_orderTransactions.Clear();
		_trackedClientOrders.Clear();
		_seenTrades.Clear();
		_orderSignatures.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastPoll = default;
		_lastPing = default;
		_lastOrderSearch = default;
		_resolvedPortfolioName = null;
		_orderStatusFilter = null;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg,
		CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		if (_socket is not null && now - _lastPing >= TimeSpan.FromSeconds(30))
		{
			await _socket.Ping(cancellationToken);
			_lastPing = now;
		}

		if (_rest is not null && now - _lastPoll >= PollingInterval)
		{
			_lastPoll = now;
			try
			{
				await PollTrackedOrders(cancellationToken);
				if (_orderStatusSubscriptionId != 0)
					await SendOrderSnapshot(_orderStatusSubscriptionId,
						cancellationToken);
				if (_portfolioSubscriptionId != 0)
					await SendPortfolioSnapshot(_portfolioSubscriptionId,
						cancellationToken);
			}
			catch (Exception error) when (
				error is not OperationCanceledException ||
				!cancellationToken.IsCancellationRequested)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask PollTrackedOrders(
		CancellationToken cancellationToken)
	{
		foreach (var orderId in _trackedClientOrders.ToArray())
		{
			try
			{
				var response = await _rest.GetOrder(orderId, cancellationToken);
				await ProcessOrderStatus(response, 0, cancellationToken);
			}
			catch (HttpRequestException error) when (
				error.StatusCode is HttpStatusCode.NotFound or
					HttpStatusCode.Conflict)
			{
				// The order can take a short time to become queryable.
			}
		}
	}

	private void DisposeClients()
	{
		if (_socket is not null)
		{
			_socket.QuoteReceived -= ProcessQuote;
			_socket.OrderBookReceived -= ProcessOrderBook;
			_socket.TradeReceived -= ProcessPublicTrade;
			_socket.CandleReceived -= ProcessCandle;
			_socket.Error -= SendOutErrorAsync;
			_socket.StateChanged -= SendOutConnectionStateAsync;
			_socket.Dispose();
			_socket = null;
		}

		_rest?.Dispose();
		_rest = null;
	}
}
