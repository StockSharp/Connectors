namespace StockSharp.Usmart;

public partial class UsmartMessageAdapter
{
	private sealed class MarketSubscription
	{
		public SecurityId SecurityId { get; set; }
		public string Topic { get; set; }
	}

	private UsmartRestClient _rest;
	private UsmartWebSocketClient _stream;
	private readonly SynchronizedDictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly SynchronizedDictionary<long, MarketSubscription> _depthSubscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, UsmartOrder> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedSet<long> _reportedTrades = [];
	private readonly CachedSynchronizedDictionary<string, SecurityId> _positionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastPoll;
	private int _pollCursor;

	/// <summary>Initializes a new instance of the <see cref="UsmartMessageAdapter"/> class.</summary>
	public UsmartMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames([
			TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
			TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
			TimeSpan.FromDays(1), TimeSpan.FromDays(7),
		]);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 10];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["SEHK", "US", "SSE", "SZSE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		ClearState();
		var token = AccessToken?.UnSecure().ThrowIfEmpty(nameof(AccessToken));
		var channel = ChannelId.ThrowIfEmpty(nameof(ChannelId));
		var privateKey = PrivateKey?.UnSecure().ThrowIfEmpty(nameof(PrivateKey));
		FundAccount.ThrowIfEmpty(nameof(FundAccount));
		DefaultMarket = DefaultMarket.IsEmpty("hk").ToLowerInvariant();
		if (DefaultMarket is not "hk" and not "us" and not "sh" and not "sz")
			throw new ArgumentOutOfRangeException(nameof(DefaultMarket), DefaultMarket,
				"uSMART market must be hk, us, sh, or sz.");

		_rest = new(token, channel, privateKey, IsDemo);
		_stream = new(IsDemo, token, Math.Max(1, ReConnectionSettings.ReAttemptCount))
		{
			Parent = this,
		};
		_stream.QuoteReceived += OnQuote;
		_stream.TickReceived += OnTick;
		_stream.DepthReceived += OnDepth;
		_stream.Error += SendOutErrorAsync;
		_stream.StateChanged += SendOutConnectionStateAsync;
		try
		{
			await _rest.GetMarketState(DefaultMarket, cancellationToken);
			await _stream.Connect(cancellationToken);
			message.SessionId = $"uSMART {FundAccount}";
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await _stream.Disconnect();
		DisposeClients();
		ClearState();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		DisposeClients();
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage message,
		CancellationToken cancellationToken)
	{
		if (_rest != null && CurrentTime - _lastPoll >= TimeSpan.FromSeconds(5))
		{
			try
			{
				if (_pollCursor++ % 2 == 0 && _orderStatusSubscriptionId != 0)
					await SendOrderSnapshot(_orderStatusSubscriptionId, null, false,
						cancellationToken);
				else if (_portfolioSubscriptionId != 0)
					await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter,
						false, cancellationToken);
			}
			catch (Exception error)
			{
				await SendOutErrorAsync(error, cancellationToken);
			}
			finally
			{
				_lastPoll = CurrentTime;
			}
		}
		await base.TimeAsync(message, cancellationToken);
	}

	private string ResolvePortfolio(string portfolioName)
	{
		var account = FundAccount.ThrowIfEmpty(nameof(FundAccount));
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(account))
			throw new InvalidOperationException(
				$"uSMART fund account '{portfolioName}' is not available in this session.");
		return account;
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.QuoteReceived -= OnQuote;
			_stream.TickReceived -= OnTick;
			_stream.DepthReceived -= OnDepth;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}
		_rest?.Dispose();
		_rest = null;
	}

	private void ClearState()
	{
		_level1Subscriptions.Clear();
		_tickSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_orders.Clear();
		_reportedTrades.Clear();
		_positionIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastPoll = default;
		_pollCursor = 0;
	}
}
