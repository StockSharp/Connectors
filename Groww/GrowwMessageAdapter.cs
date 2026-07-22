namespace StockSharp.Groww;

public partial class GrowwMessageAdapter
{
	private enum FeedKinds
	{
		Price,
		Index,
		Depth,
		Order,
		Position,
	}

	private sealed class FeedRoute
	{
		public FeedKinds Kind { get; init; }
		public string SecurityKey { get; init; }
	}

	private sealed class MarketSubscription
	{
		public SecurityId SecurityId { get; init; }
		public GrowwSecurityInfo Security { get; init; }
		public long Level1Id { get; set; }
		public long TickId { get; set; }
		public long DepthId { get; set; }
		public (long timestamp, decimal price)? LastTick { get; set; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public GrowwSecurityInfo Security { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Price { get; init; }
		public decimal Volume { get; init; }
	}

	private GrowwRestClient _rest;
	private GrowwFeedClient _feed;
	private readonly SynchronizedDictionary<string, MarketSubscription> _marketSubscriptions = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, FeedRoute> _feedRoutes = new(StringComparer.Ordinal);
	private readonly SynchronizedDictionary<string, GrowwSecurityInfo> _securityInfos = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _orderFills = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioName;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;
	private bool _isInstrumentCacheLoaded;

	/// <summary>Initializes a new instance of the <see cref="GrowwMessageAdapter"/> class.</summary>
	public GrowwMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(GrowwExtensions.TimeFrames);
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
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [1, 5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["NSE", "BSE", "MCX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		if ((Token?.Length ?? 0) == 0 && (Key?.Length ?? 0) == 0)
			throw new InvalidOperationException("Set either a Groww access token or API key credentials.");

		var attempts = Math.Max(1, ReConnectionSettings.ReAttemptCount);
		_rest = new(Token, Key, Secret, TotpSecret, attempts) { Parent = this };
		try
		{
			await _rest.Connect(cancellationToken);
			_feed = new(_rest, attempts) { Parent = this };
			_feed.DataReceived += OnFeedData;
			_feed.Error += SendOutErrorAsync;
			_feed.StateChanged += SendOutConnectionStateAsync;
			await _feed.Connect(cancellationToken);

			if (this.IsTransactional())
				await SubscribePrivateFeeds(cancellationToken);

			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients();
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg, CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
		await DisposeClients();
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		if (_portfolioSubscriptionId != 0 && CurrentTime - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(_portfolioSubscriptionId, cancellationToken);
		if (_orderStatusSubscriptionId != 0 && CurrentTime - _lastOrderRefresh >= TimeSpan.FromSeconds(30))
			await SendOrderSnapshot(_orderStatusSubscriptionId, false, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await DisposeClients();
		_marketSubscriptions.Clear();
		_feedRoutes.Clear();
		_securityInfos.Clear();
		_orders.Clear();
		_orderFills.Clear();
		_tradeIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioName = null;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		_isInstrumentCacheLoaded = false;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	private async ValueTask SubscribePrivateFeeds(CancellationToken cancellationToken)
	{
		foreach (var (subject, kind) in new[]
		{
			(GrowwFeedTopics.GetEquityOrders(_feed.SubscriptionId), FeedKinds.Order),
			(GrowwFeedTopics.GetDerivativeOrders(_feed.SubscriptionId), FeedKinds.Order),
			(GrowwFeedTopics.GetDerivativePositions(_feed.SubscriptionId), FeedKinds.Position),
		})
		{
			_feedRoutes[subject] = new() { Kind = kind };
			await _feed.Subscribe(subject, cancellationToken);
		}
	}

	private async ValueTask DisposeClients()
	{
		if (_feed != null)
		{
			_feed.DataReceived -= OnFeedData;
			_feed.Error -= SendOutErrorAsync;
			_feed.StateChanged -= SendOutConnectionStateAsync;
			await _feed.Disconnect();
			_feed.Dispose();
			_feed = null;
		}
		_rest?.Dispose();
		_rest = null;
	}

	private ValueTask OnFeedData(string subject, byte[] data, CancellationToken cancellationToken)
	{
		if (!_feedRoutes.TryGetValue(subject, out var route))
			return default;
		return route.Kind switch
		{
			FeedKinds.Price or FeedKinds.Index or FeedKinds.Depth => ProcessMarketFeed(route, data, cancellationToken),
			FeedKinds.Order => ProcessOrderFeed(data, cancellationToken),
			FeedKinds.Position => ProcessPositionFeed(data, cancellationToken),
			_ => default,
		};
	}

	private GrowwSecurityInfo ResolveSecurity(SecurityId securityId, SecurityTypes? securityType = null)
	{
		var key = GetSecurityKey(securityId);
		if (_securityInfos.TryGetValue(key, out var info))
			return info;
		info = securityId.ToGroww(securityType);
		CacheSecurity(info);
		return info;
	}

	private void CacheSecurity(GrowwSecurityInfo info)
	{
		if (info == null)
			return;
		_securityInfos[$"SYMBOL:{info.Exchange}:{info.TradingSymbol}"] = info;
		if (!info.ExchangeToken.IsEmpty())
			_securityInfos[$"TOKEN:{info.Exchange}:{info.Segment}:{info.ExchangeToken}"] = info;
		if (!info.Isin.IsEmpty())
			_securityInfos[$"CONTRACT:{info.Isin}"] = info;
		if (!info.TradingSymbol.IsEmpty())
			_securityInfos[$"CONTRACT:{info.TradingSymbol}"] = info;
	}

	private GrowwSecurityInfo FindSecurity(string exchange, string tradingSymbol, string contractId = null, string segment = null)
	{
		if (!contractId.IsEmpty() && _securityInfos.TryGetValue($"CONTRACT:{contractId}", out var info))
			return info;
		if (!tradingSymbol.IsEmpty() && _securityInfos.TryGetValue($"SYMBOL:{exchange}:{tradingSymbol}", out info))
			return info;
		return new()
		{
			Exchange = exchange.IsEmpty("NSE"),
			Segment = segment.IsEmpty("CASH"),
			TradingSymbol = tradingSymbol.IsEmpty(contractId),
			GrowwSymbol = $"{exchange.IsEmpty("NSE")}-{tradingSymbol.IsEmpty(contractId)}",
			Isin = contractId,
		};
	}

	private static SecurityId ToSecurityId(GrowwSecurityInfo info)
		=> new()
		{
			SecurityCode = info.TradingSymbol.IsEmpty(info.Isin),
			BoardCode = info.Exchange.IsEmpty("NSE"),
			Native = info.ToNative(),
		};

	private static string GetSecurityKey(SecurityId securityId)
		=> $"SYMBOL:{securityId.BoardCode}:{securityId.SecurityCode}";
}
