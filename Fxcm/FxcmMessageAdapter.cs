namespace StockSharp.Fxcm;

public partial class FxcmMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string Symbol { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
		public FxcmOrderCondition Condition { get; init; }
	}

	private FxcmRestClient _rest;
	private FxcmSocketClient _stream;
	private string _token;
	private readonly CachedSynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, FxcmOffer> _offers = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, FxcmOffer> _offersById = [];
	private readonly SynchronizedDictionary<long, OrderTracker> _orders = [];
	private readonly SynchronizedDictionary<long, FxcmPosition> _positions = [];
	private readonly SynchronizedDictionary<string, FxcmAccount> _accounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _subscribedModels = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _reportedFills = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;

	/// <summary>Initializes a new instance of the <see cref="FxcmMessageAdapter"/> class.</summary>
	public FxcmMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(FxcmExtensions.TimeFrames.Keys);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType.IsTFCandles || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsSupportExecutionsPnL => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = [FxcmExtensions.BoardCode];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_stream != null || _rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_token = Token?.UnSecure().ThrowIfEmpty(nameof(Token));
		_stream = new(IsDemo, _token, Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		_stream.SessionConnected += OnSessionConnected;
		_stream.PriceReceived += ProcessPrice;
		_stream.OrderReceived += ProcessOrderUpdate;
		_stream.PositionReceived += ProcessPositionUpdate;
		_stream.AccountReceived += ProcessAccountUpdate;
		_stream.Error += SendOutErrorAsync;
		_stream.StateChanged += SendOutConnectionStateAsync;

		try
		{
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
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_stream == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		await _stream.Disconnect();
		DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		DisposeClients();
		_marketSubscriptions.Clear();
		_offers.Clear();
		_offersById.Clear();
		_orders.Clear();
		_positions.Clear();
		_accounts.Clear();
		_subscribedModels.Clear();
		_reportedFills.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask OnSessionConnected(string sessionId, CancellationToken cancellationToken)
	{
		var rest = new FxcmRestClient(IsDemo, sessionId + _token,
			Math.Max(1, ReConnectionSettings.ReAttemptCount)) { Parent = this };
		var old = Interlocked.Exchange(ref _rest, rest);
		old?.Dispose();

		foreach (var symbol in _marketSubscriptions.CachedValues.Select(s => s.Symbol)
			.Distinct(StringComparer.OrdinalIgnoreCase))
			await rest.SubscribePair(symbol, cancellationToken);

		foreach (var model in GetDesiredModels())
		{
			await rest.SubscribeModel(model, cancellationToken);
			_subscribedModels.Add(model);
		}
	}

	private async ValueTask RefreshModelSubscriptions(CancellationToken cancellationToken)
	{
		var rest = GetRest();
		var desired = GetDesiredModels().ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var model in desired.Where(model => !_subscribedModels.Contains(model)))
		{
			await rest.SubscribeModel(model, cancellationToken);
			_subscribedModels.Add(model);
		}
		foreach (var model in _subscribedModels.SyncGet(set => set.ToArray())
			.Where(model => !desired.Contains(model)))
		{
			await rest.UnsubscribeModel(model, cancellationToken);
			_subscribedModels.Remove(model);
		}
	}

	private IEnumerable<string> GetDesiredModels()
	{
		if (_orderStatusSubscriptionId != 0)
		{
			yield return FxcmModelNames.Order;
			yield return FxcmModelNames.ClosedPosition;
			yield return FxcmModelNames.OpenPosition;
		}
		if (_portfolioSubscriptionId != 0)
		{
			yield return FxcmModelNames.Account;
			yield return FxcmModelNames.OpenPosition;
		}
	}

	private FxcmRestClient GetRest()
		=> _rest ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void CacheOffer(FxcmOffer offer)
	{
		if (offer?.Symbol.IsEmpty() != false || offer.OfferId <= 0)
			return;
		_offers[offer.Symbol] = offer;
		_offersById[offer.OfferId] = offer;
	}

	private static string GetPortfolioName(FxcmAccount account)
		=> account?.AccountName.IsEmpty(account?.AccountId).IsEmpty("FXCM");

	private static string GetPortfolioName(FxcmPosition position)
		=> position?.AccountName.IsEmpty(position?.AccountId).IsEmpty("FXCM");

	private void DisposeClients()
	{
		if (_stream != null)
		{
			_stream.SessionConnected -= OnSessionConnected;
			_stream.PriceReceived -= ProcessPrice;
			_stream.OrderReceived -= ProcessOrderUpdate;
			_stream.PositionReceived -= ProcessPositionUpdate;
			_stream.AccountReceived -= ProcessAccountUpdate;
			_stream.Error -= SendOutErrorAsync;
			_stream.StateChanged -= SendOutConnectionStateAsync;
			_stream.Dispose();
			_stream = null;
		}

		Interlocked.Exchange(ref _rest, null)?.Dispose();
		_token = null;
		_subscribedModels.Clear();
	}
}
