namespace StockSharp.PhillipPoems;

public partial class PhillipPoemsMessageAdapter
{
	private sealed class MarketSubscription
	{
		public SecurityId SecurityId { get; set; }
		public PoemsCounter Counter { get; set; }
		public int MaxDepth { get; set; }
	}

	private sealed class TickSubscription
	{
		public SecurityId SecurityId { get; set; }
		public PoemsCounter Counter { get; set; }
		public Dictionary<string, int> SeenCounts { get; } = new(StringComparer.Ordinal);
		public DateTime TradeDate { get; set; }
	}

	private PhillipPoemsClient _client;
	private PoemsMarket[] _markets = [];
	private readonly CachedSynchronizedDictionary<string, PoemsCounter> _counters =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly SynchronizedDictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly SynchronizedDictionary<long, MarketSubscription> _depthSubscriptions = [];
	private readonly SynchronizedDictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedDictionary<string, long> _cancelTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, string> _orderSignatures =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, decimal> _filledQuantities =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, PoemsOrder> _orders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly CachedSynchronizedDictionary<string, SecurityId> _positionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastPoll;
	private int _pollCursor;
	private int _level1Cursor;
	private int _tickCursor;
	private int _depthCursor;

	/// <summary>Initializes a new instance of the <see cref="PhillipPoemsMessageAdapter"/> class.</summary>
	public PhillipPoemsMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Ticks ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5, 10, 20];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		["SGX", "NYSE", "AMEX", "NASD", "LSE", "TSE", "KLSE", "SEHK", "SSE", "SZSE"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message,
		CancellationToken cancellationToken)
	{
		if (_client != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		ClearState();
		var accessToken = Token?.UnSecure();
		var refreshToken = RefreshToken?.UnSecure();
		if (accessToken.IsEmpty() && refreshToken.IsEmpty())
			throw new InvalidOperationException(
				"A POEMS access token or refresh token is required.");
		if (!refreshToken.IsEmpty() &&
			(Key.IsEmpty() || Secret?.UnSecure().IsEmpty() != false))
			throw new InvalidOperationException(
				"POEMS client credentials are required when a refresh token is configured.");

		var client = new PhillipPoemsClient(IsDemo,
			ApiKey?.UnSecure().ThrowIfEmpty(nameof(ApiKey)), Key?.UnSecure(),
			Secret?.UnSecure(), accessToken, refreshToken);
		_client = client;
		try
		{
			var marketResponse = await client.GetMarkets(cancellationToken);
			_markets = marketResponse.Markets ?? [];
			if (_markets.Length == 0)
				throw new InvalidOperationException(
					"POEMS returned no stock exchanges for this OAuth session.");
			if (!client.AccountNo.IsEmpty())
			{
				if (!AccountNo.IsEmpty() && !AccountNo.EqualsIgnoreCase(client.AccountNo))
					throw new InvalidOperationException(
						$"Configured POEMS account '{AccountNo}' does not match token account '{client.AccountNo}'.");
				AccountNo = client.AccountNo;
			}
			AccountNo.ThrowIfEmpty(nameof(AccountNo));
			if (!client.AccountType.IsEmpty())
				AccountType = client.AccountType;
			SyncTokens();
			message.SessionId = $"Phillip POEMS {AccountNo}";
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
		if (_client != null && CurrentTime - _lastPoll >= PollingInterval)
		{
			try
			{
				await RunNextPollingJob(cancellationToken);
				SyncTokens();
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

	private async Task RunNextPollingJob(CancellationToken cancellationToken)
	{
		for (var i = 0; i < 5; i++)
		{
			switch (_pollCursor++ % 5)
			{
				case 0 when _orderStatusSubscriptionId != 0:
					await SendOrderSnapshot(_orderStatusSubscriptionId, null, false,
						cancellationToken);
					return;
				case 1 when _portfolioSubscriptionId != 0:
					await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter,
						false, cancellationToken);
					return;
				case 2 when _level1Subscriptions.Count > 0:
					await RefreshLevel1(cancellationToken);
					return;
				case 3 when _tickSubscriptions.Count > 0:
					await RefreshNextTicks(cancellationToken);
					return;
				case 4 when _depthSubscriptions.Count > 0:
					await RefreshNextDepth(cancellationToken);
					return;
			}
		}
	}

	private string ResolvePortfolio(string portfolioName)
	{
		var accountNo = AccountNo.ThrowIfEmpty(nameof(AccountNo));
		if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(accountNo))
			throw new InvalidOperationException(
				$"POEMS account '{portfolioName}' is not available in this adapter session.");
		return accountNo;
	}

	private async Task<PoemsCounter> ResolveCounter(SecurityId securityId,
		CancellationToken cancellationToken)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
		var exchange = securityId.BoardCode.IsEmpty(DefaultExchange).ToNativeExchange()
			.ThrowIfEmpty(nameof(DefaultExchange));
		var key = PhillipPoemsExtensions.GetSecurityKey(
			new SecurityId { SecurityCode = code, BoardCode = exchange }, DefaultExchange);
		if (_counters.TryGetValue(key, out var cached))
			return cached;

		var response = await _client.GetCounterId(exchange, code, cancellationToken);
		var identity = PhillipPoemsExtensions.ParseCounterId(response.CounterId)
			?? throw new InvalidOperationException(
				$"POEMS returned an invalid counter ID for '{code}@{exchange}'.");
		var counter = new PoemsCounter
		{
			CounterId = response.CounterId,
			Code = identity.Code,
			Symbol = code,
			Market = identity.Market.IsEmpty(DefaultMarket),
			Exchange = identity.Exchange.IsEmpty(exchange),
			Product = identity.Product.IsEmpty("ST"),
		};
		CacheCounter(counter);
		return counter;
	}

	private void CacheCounters(IEnumerable<PoemsCounter> counters)
	{
		foreach (var counter in counters ?? [])
			CacheCounter(counter);
	}

	private void CacheCounter(PoemsCounter counter)
	{
		if (counter == null)
			return;
		var identity = PhillipPoemsExtensions.ParseCounterId(counter.CounterId);
		var exchange = counter.Exchange.IsEmpty(identity?.Exchange).IsEmpty(DefaultExchange)
			.ToNativeExchange();
		if (!counter.CounterId.IsEmpty())
			_counters[counter.CounterId] = counter;
		foreach (var code in new[] { counter.Symbol, counter.Code, identity?.Code }
			.Where(code => !code.IsEmpty()).Distinct(StringComparer.OrdinalIgnoreCase))
			_counters[$"{code}@{exchange}"] = counter;
	}

	private PoemsCounter CachePrice(PoemsPrice price)
	{
		if (price == null)
			return null;
		var identity = PhillipPoemsExtensions.ParseCounterId(price.CounterId);
		var counter = new PoemsCounter
		{
			CounterId = price.CounterId,
			Code = identity?.Code,
			Symbol = price.Symbol,
			Name = price.CounterName,
			Market = price.Market.IsEmpty(identity?.Market),
			Exchange = price.Exchange.IsEmpty(identity?.Exchange),
			Product = price.Product,
			ProductIcon = price.ProductIcon,
			PmpTopic = price.PmpTopic,
		};
		CacheCounter(counter);
		return counter;
	}

	private void SyncTokens()
	{
		if (_client == null)
			return;
		if (!_client.AccessToken.IsEmpty())
			Token = _client.AccessToken.Secure();
		if (!_client.RefreshToken.IsEmpty())
			RefreshToken = _client.RefreshToken.Secure();
	}

	private void DisposeClient()
	{
		SyncTokens();
		_client?.Dispose();
		_client = null;
	}

	private void ClearState()
	{
		_markets = [];
		_counters.Clear();
		_level1Subscriptions.Clear();
		_tickSubscriptions.Clear();
		_depthSubscriptions.Clear();
		_orderTransactions.Clear();
		_transactionOrders.Clear();
		_cancelTransactions.Clear();
		_orderSignatures.Clear();
		_filledQuantities.Clear();
		_orders.Clear();
		_positionIds.Clear();
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastPoll = default;
		_pollCursor = 0;
		_level1Cursor = 0;
		_tickCursor = 0;
		_depthCursor = 0;
	}
}
