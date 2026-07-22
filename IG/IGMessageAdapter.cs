namespace StockSharp.IG;

using StockSharp.IG.Native;

public partial class IgMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Epic { get; init; }
		public DataType DataType { get; init; }
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
		public IgDealKinds Kind { get; init; }
		public IgOrderCondition Condition { get; init; }
		public string DealReference { get; init; }
		public string DealId { get; set; }
	}

	private IgRestClient _rest;
	private IgStreamingClient _stream;
	private IgSession _session;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly CachedSynchronizedDictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly SynchronizedDictionary<string, IgMarketDetails> _markets = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orderReferences = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orderDeals = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, IgDealKinds> _dealKinds = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeEvents = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="IgMessageAdapter"/> class.</summary>
	public IgMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(IgExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.PositionChanges ||
			dataType == DataType.Transactions || dataType.IsTFCandles || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["IG"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		_rest = new(Environment, ApiKey, Login, Password?.UnSecure(), EncryptPassword,
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		try
		{
			_session = await _rest.Login(AccountId, cancellationToken);
			_stream = new(_session) { Parent = this };
			_stream.MarketReceived += OnMarketReceived;
			_stream.TickReceived += OnTickReceived;
			_stream.CandleReceived += OnCandleReceived;
			_stream.AccountReceived += OnAccountReceived;
			_stream.TradeReceived += OnTradeReceived;
			_stream.Error += OnStreamError;
			_stream.StateChanged += OnStreamStateChanged;
			await _stream.Connect(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		_stream?.Disconnect();
		await _rest.Logout(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_candleSubscriptions.Clear();
		_markets.Clear();
		_orderReferences.Clear();
		_orderDeals.Clear();
		_dealKinds.Clear();
		_tradeEvents.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private void OnStreamError(Exception error)
		=> _ = SendOutErrorAsync(error, CancellationToken.None).AsTask();

	private void OnStreamStateChanged(ConnectionStates state)
		=> _ = SendOutConnectionStateAsync(state, CancellationToken.None).AsTask();

	private void RunStreamHandler(ValueTask task)
		=> _ = ObserveStreamHandler(task);

	private async Task ObserveStreamHandler(ValueTask task)
	{
		try
		{
			await task;
		}
		catch (Exception ex)
		{
			await SendOutErrorAsync(ex, CancellationToken.None);
		}
	}

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.MarketReceived -= OnMarketReceived;
			_stream.TickReceived -= OnTickReceived;
			_stream.CandleReceived -= OnCandleReceived;
			_stream.AccountReceived -= OnAccountReceived;
			_stream.TradeReceived -= OnTradeReceived;
			_stream.Error -= OnStreamError;
			_stream.StateChanged -= OnStreamStateChanged;
			_stream.Dispose();
			_stream = null;
		}
		_rest?.Dispose();
		_rest = null;
		_session = null;
	}
}
