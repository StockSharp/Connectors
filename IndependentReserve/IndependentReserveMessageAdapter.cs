namespace StockSharp.IndependentReserve;

public partial class IndependentReserveMessageAdapter
{
	private sealed class MarketDefinition
	{
		public string Symbol { get; init; }
		public string PrimaryCurrency { get; init; }
		public string SecondaryCurrency { get; init; }
		public string Name { get; init; }
		public decimal PriceStep { get; init; }
		public decimal VolumeStep { get; init; }
	}

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime OpenTime { get; set; }
		public decimal Open { get; set; }
		public decimal High { get; set; }
		public decimal Low { get; set; }
		public decimal Close { get; set; }
		public decimal Volume { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
		public string ExchangeOrderId { get; set; }
		public string ClientOrderId { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public bool IsPostOnly { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public IndependentReserveOrderCondition Condition { get; init; }
	}

	private sealed class BookOrder
	{
		public Guid Id { get; init; }
		public decimal Price { get; set; }
		public decimal Volume { get; set; }
	}

	private sealed class BufferedBookEvent
	{
		public IndependentReserveSocketEvents Event { get; init; }
		public IndependentReserveSocketPayload Payload { get; init; }
		public DateTime Time { get; init; }
		public long Nonce { get; init; }
	}

	private sealed class BookState
	{
		public string Symbol { get; init; }
		public Dictionary<Guid, BookOrder> Bids { get; } = [];
		public Dictionary<Guid, BookOrder> Asks { get; } = [];
		public List<BufferedBookEvent> Buffer { get; } = [];
		public bool IsInitialized { get; set; }
		public DateTime Timestamp { get; set; }
		public long Nonce { get; set; }
	}

	private readonly record struct BalanceFingerprint(decimal Total,
		decimal Available);
	private readonly record struct OrderFingerprint(
		IndependentReserveOrderStatuses Status, decimal Filled,
		decimal Volume, decimal? AveragePrice);

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _privateRefreshGate = new(1, 1);
	private readonly Dictionary<string, MarketDefinition> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, MarketDefinition[]> _primaryMarkets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<string, int> _channelReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _accountChannels =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private IndependentReserveRestClient _restClient;
	private IndependentReserveSocketClient _socketClient;
	private CancellationTokenSource _pollingCancellation;
	private Task _pollingTask;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="IndependentReserveMessageAdapter"/> class.
	/// </summary>
	public IndependentReserveMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.IndependentReserve];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.IndependentReserve) ||
			securityId.IsAssociated(BoardCodes.IndependentReserve);

	private IndependentReserveRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private IndependentReserveSocketClient SocketClient
		=> _socketClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Independent Reserve API key and secret are required for private operations.");
	}

	private string GetPortfolioName() => $"IndependentReserve_{Key.ToId()}";

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Independent Reserve portfolio '{portfolioName}'.");
	}

	private void RegisterMarkets(
		IEnumerable<IndependentReserveCurrencyConfig> primaryCurrencies,
		IEnumerable<string> secondaryCurrencies)
	{
		var markets = new List<MarketDefinition>();
		foreach (var primary in primaryCurrencies ?? [])
		{
			if (primary?.Currency.IsEmpty() != false || !primary.IsTradeEnabled)
				continue;
			foreach (var secondaryValue in secondaryCurrencies ?? [])
			{
				if (secondaryValue.IsEmpty())
					continue;
				var primaryCode = primary.Currency.Trim().ToUpperInvariant();
				var secondaryCode = secondaryValue.Trim().ToUpperInvariant();
				markets.Add(new()
				{
					Symbol = IndependentReserveExtensions.ToSymbol(primaryCode,
						secondaryCode),
					PrimaryCurrency = primaryCode,
					SecondaryCurrency = secondaryCode,
					Name = primary.Name,
					VolumeStep = IndependentReserveExtensions.StepFromScale(
						primary.DecimalPlaces?.OrderPrimaryCurrency ?? 0),
					PriceStep = IndependentReserveExtensions.StepFromScale(
						primary.DecimalPlaces?.OrderSecondaryCurrency ?? 0),
				});
			}
		}

		using (_sync.EnterScope())
		{
			_markets.Clear();
			_primaryMarkets.Clear();
			foreach (var market in markets)
				_markets[market.Symbol] = market;
			foreach (var group in markets.GroupBy(static value =>
				value.PrimaryCurrency, StringComparer.OrdinalIgnoreCase))
				_primaryMarkets[group.Key] = [.. group];
		}
	}

	private MarketDefinition GetMarket(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty() &&
			!securityId.BoardCode.EqualsIgnoreCase(
				BoardCodes.IndependentReserve) &&
			!securityId.IsAssociated(BoardCodes.IndependentReserve))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Independent Reserve.");
		return GetMarket(securityId.SecurityCode);
	}

	private MarketDefinition GetMarket(string symbol)
	{
		var parts = symbol.SplitSymbol();
		symbol = IndependentReserveExtensions.ToSymbol(parts.primary,
			parts.secondary);
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Independent Reserve market '{symbol}'.");
	}

	private MarketDefinition[] GetMarketsByPrimary(string primary)
	{
		if (primary.IsEmpty())
			return [];
		using (_sync.EnterScope())
			return _primaryMarkets.TryGetValue(primary, out var markets)
				? markets
				: [];
	}

	private static string GetTickerChannel(string primary)
		=> $"ticker-{primary.Trim().ToLowerInvariant()}";

	private static string GetOrderBookChannel(string primary)
		=> $"orderbook-{primary.Trim().ToLowerInvariant()}";

	private static string GetChannelPrimary(string channel)
	{
		if (channel.IsEmpty())
			return null;
		var separator = channel.IndexOf('-');
		return separator >= 0 && separator < channel.Length - 1
			? channel[(separator + 1)..].ToUpperInvariant()
			: null;
	}

	private bool AddChannelReference(string channel)
	{
		using (_sync.EnterScope())
		{
			if (_channelReferences.TryGetValue(channel, out var count))
			{
				_channelReferences[channel] = count + 1;
				return false;
			}
			_channelReferences.Add(channel, 1);
			return !_accountChannels.Contains(channel);
		}
	}

	private bool ReleaseChannelReference(string channel)
	{
		using (_sync.EnterScope())
		{
			if (!_channelReferences.TryGetValue(channel, out var count))
				return false;
			if (count > 1)
			{
				_channelReferences[channel] = count - 1;
				return false;
			}
			_channelReferences.Remove(channel);
			return !_accountChannels.Contains(channel);
		}
	}

	private async ValueTask AcquireChannelsAsync(IEnumerable<string> channels,
		CancellationToken cancellationToken)
	{
		var subscribe = channels.Where(AddChannelReference).Distinct(
			StringComparer.OrdinalIgnoreCase).ToArray();
		if (subscribe.Length > 0)
			await SocketClient.SubscribeAsync(subscribe, cancellationToken);
	}

	private async ValueTask ReleaseChannelsAsync(IEnumerable<string> channels,
		CancellationToken cancellationToken)
	{
		var unsubscribe = channels.Where(ReleaseChannelReference).Distinct(
			StringComparer.OrdinalIgnoreCase).ToArray();
		if (unsubscribe.Length > 0)
			await SocketClient.UnsubscribeAsync(unsubscribe, cancellationToken);
	}

	private void TrackOrder(TrackedOrder order, params string[] identifiers)
	{
		if (order is null)
			return;
		using (_sync.EnterScope())
		{
			foreach (var identifier in identifiers.Where(static value =>
				!value.IsEmpty()))
				_trackedOrders[identifier] = order;
			if (!order.ExchangeOrderId.IsEmpty())
				_trackedOrders[order.ExchangeOrderId] = order;
			if (!order.ClientOrderId.IsEmpty())
				_trackedOrders[order.ClientOrderId] = order;
		}
	}

	private TrackedOrder GetTrackedOrder(string identifier)
	{
		if (identifier.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(identifier, out var order)
				? order
				: null;
	}

	private bool AddPublicTrade(Guid tradeId, long transactionId)
	{
		if (tradeId == Guid.Empty)
			return false;
		using (_sync.EnterScope())
		{
			if (_seenPublicTrades.Count > 100000)
				_seenPublicTrades.Clear();
			return _seenPublicTrades.Add($"{transactionId}:{tradeId:D}");
		}
	}

	private bool AddAccountTrade(Guid tradeId, long transactionId)
	{
		if (tradeId == Guid.Empty)
			return false;
		using (_sync.EnterScope())
		{
			if (_seenAccountTrades.Count > 100000)
				_seenAccountTrades.Clear();
			return _seenAccountTrades.Add($"{transactionId}:{tradeId:D}");
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_primaryMarkets.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_channelReferences.Clear();
			_accountChannels.Clear();
			_books.Clear();
			_trackedOrders.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_portfolioSubscriptions.Clear();
			_orderSubscriptions.Clear();
			_balanceFingerprints.Clear();
			_orderFingerprints.Clear();
		}
	}

	private static string ResolveOrderIdentifier(long? numericOrderId,
		string stringOrderId, string operation)
	{
		if (!stringOrderId.IsEmpty())
			return stringOrderId.Trim();
		if (numericOrderId is > 0)
			throw new InvalidOperationException(
				$"Independent Reserve {operation} requires a GUID string order ID.");
		throw new InvalidOperationException(
			$"Independent Reserve {operation} requires an exchange order ID.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_privateRefreshGate.Dispose();
		base.DisposeManaged();
	}
}
