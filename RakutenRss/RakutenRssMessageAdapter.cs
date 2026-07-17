namespace StockSharp.RakutenRss;

[SupportedOSPlatform("windows")]
public partial class RakutenRssMessageAdapter
{
	private enum FeedKinds
	{
		Level1,
		Depth,
		Ticks,
		Candles,
	}

	private sealed class MarketSubscription
	{
		public long FeedId { get; set; }
		public SecurityId SecurityId { get; set; }
		public FeedKinds Kind { get; set; }
		public int MaxDepth { get; set; }
		public DataType DataType { get; set; }
		public string Signature { get; set; }
		public HashSet<string> TradeIds { get; } = new(StringComparer.OrdinalIgnoreCase);
	}

	private RakutenRssClient _client;
	private readonly SynchronizedDictionary<long, MarketSubscription> _subscriptions = [];
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<int, long> _requestTransactions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, bool> _orderDerivatives =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, SecurityId> _positionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastMarketPoll;
	private DateTime _lastAccountPoll;
	private int _requestId;

	/// <summary>Initializes a new instance of the <see cref="RakutenRssMessageAdapter"/> class.</summary>
	public RakutenRssMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames([
			TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3),
			TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10),
			TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
			TimeSpan.FromHours(2), TimeSpan.FromHours(4), TimeSpan.FromHours(8),
			TimeSpan.FromDays(1), TimeSpan.FromDays(7), TimeSpan.FromDays(30),
		]);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } =
		Enumerable.Range(1, 10).ToArray();

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [BoardCodes.Tse, "JAX", "JNX", "OSE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (!OperatingSystem.IsWindows())
			throw new PlatformNotSupportedException("MARKETSPEED II RSS requires Windows and desktop Excel.");
		PortfolioName.ThrowIfEmpty(nameof(PortfolioName));
		_client = new();
		try
		{
			await _client.Open(IsExcelVisible, MaxTableRows, cancellationToken);
			message.SessionId = "Rakuten MARKETSPEED II RSS";
			await base.ConnectAsync(message, cancellationToken);
		}
		catch
		{
			DisposeClient();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_client == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		DisposeClient();
		ClearState();
		await base.DisconnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message,
		CancellationToken cancellationToken)
	{
		DisposeClient();
		ClearState();
		await base.ResetAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage message,
		CancellationToken cancellationToken)
	{
		if (_client != null && CurrentTime - _lastMarketPoll >= HeartbeatInterval)
		{
			_lastMarketPoll = CurrentTime;
			foreach (var pair in _subscriptions.ToArray())
			{
				try { await PollSubscription(pair.Key, pair.Value, cancellationToken); }
				catch (Exception error) { await SendOutErrorAsync(error, cancellationToken); }
			}
		}
		if (_client != null && CurrentTime - _lastAccountPoll >= TimeSpan.FromSeconds(2) &&
			(_orderStatusSubscriptionId != 0 || _portfolioSubscriptionId != 0))
		{
			_lastAccountPoll = CurrentTime;
			try
			{
				if (_orderStatusSubscriptionId != 0)
					await SendOrderSnapshot(_orderStatusSubscriptionId, false, cancellationToken);
				if (_portfolioSubscriptionId != 0)
					await SendPortfolioSnapshot(_portfolioSubscriptionId, false, cancellationToken);
			}
			catch (Exception error) { await SendOutErrorAsync(error, cancellationToken); }
		}
		await base.TimeAsync(message, cancellationToken);
	}

	private void DisposeClient()
	{
		_client?.Dispose();
		_client = null;
	}

	private void ClearState()
	{
		_subscriptions.Clear();
		_transactionOrders.Clear();
		_requestTransactions.Clear();
		_orderTransactions.Clear();
		_orderDerivatives.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_tradeSignatures.Clear();
		_positionIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastMarketPoll = default;
		_lastAccountPoll = default;
		_requestId = 0;
	}
}
