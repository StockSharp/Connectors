namespace StockSharp.CapitalCom;

internal enum CapitalComDealKinds
{
	Position,
	WorkingOrder,
}

public partial class CapitalComMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Epic { get; init; }
	}

	private sealed class CandleSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Epic { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Portfolio { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
		public CapitalComDealKinds Kind { get; init; }
		public CapitalComOrderCondition Condition { get; init; }
		public string DealReference { get; init; }
		public string DealId { get; set; }
	}

	private CapitalComRestClient _rest;
	private CapitalComWebSocketClient _stream;
	private CapitalComSession _session;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly CachedSynchronizedDictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<long, CapitalComOhlc> _lastLiveCandles = [];
	private readonly SynchronizedDictionary<string, CapitalComMarketDetails> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orderReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orderDeals =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, CapitalComDealKinds> _dealKinds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeEvents = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private DateTime _lastSessionPing;

	/// <summary>Initializes a new instance of the <see cref="CapitalComMessageAdapter"/> class.</summary>
	public CapitalComMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(3);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(CapitalComExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.PositionChanges || dataType == DataType.Transactions ||
			dataType.IsTFCandles || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["CAPITALCOM"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_rest = new(IsDemo, ApiKey, Login, Password?.UnSecure(), IsPasswordEncryptionEnabled,
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		try
		{
			_session = await _rest.Login(AccountId, cancellationToken);
			_stream = new(_session, Math.Max(1, ReConnectionSettings.ReAttemptCount),
				ReConnectionSettings.WorkingTime)
			{
				Parent = this,
			};
			_stream.QuoteReceived += ProcessQuote;
			_stream.OhlcReceived += ProcessOhlc;
			_stream.Error += SendOutErrorAsync;
			_stream.StateChanged += SendOutConnectionStateAsync;
			await _stream.ConnectAsync(cancellationToken);
			_lastSessionPing = DateTime.UtcNow;
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		try
		{
			_stream?.Disconnect();
			await _rest.Logout(cancellationToken);
			await base.DisconnectAsync(disconnectMsg, cancellationToken);
		}
		finally
		{
			DisposeClients();
		}
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_candleSubscriptions.Clear();
		_lastLiveCandles.Clear();
		_markets.Clear();
		_orderReferences.Clear();
		_orderDeals.Clear();
		_dealKinds.Clear();
		_tradeEvents.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		_lastSessionPing = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var now = DateTime.UtcNow;
		if (_rest != null && now - _lastSessionPing >= TimeSpan.FromMinutes(5))
		{
			await _rest.Ping(cancellationToken);
			if (_stream != null)
				await _stream.Ping(cancellationToken);
			_lastSessionPing = now;
		}

		if (_orderStatusSubscriptionId != 0 &&
			now - _lastOrderRefresh >= TimeSpan.FromSeconds(3))
		{
			await SendCurrentOrders(_orderStatusSubscriptionId, cancellationToken);
			_lastOrderRefresh = now;
		}

		if (_portfolioSubscriptionId != 0 &&
			now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(15))
		{
			await SendPortfolioSnapshot(_portfolioSubscriptionId, null, cancellationToken);
			_lastPortfolioRefresh = now;
		}

		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.QuoteReceived -= ProcessQuote;
			_stream.OhlcReceived -= ProcessOhlc;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}

		_rest?.Dispose();
		_rest = null;
		_session = null;
	}
}
