namespace StockSharp.Shioaji;

public partial class ShioajiMessageAdapter
{
	private sealed class MarketSubscription
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public ShioajiContract Contract { get; init; }
		public ShioajiMarketSubscriptionRequest Request { get; init; }
		public DataType DataType { get; init; }
	}

	private sealed class OrderTracker
	{
		public long TransactionId { get; init; }
		public SecurityId SecurityId { get; init; }
		public string PortfolioName { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public TimeInForce TimeInForce { get; init; }
		public ShioajiOrderCondition Condition { get; init; }
	}

	private static readonly TimeSpan[] _timeFrames = [TimeSpan.FromMinutes(1)];
	private static readonly string[] _nativeSecurityTypes = ["STK", "IND", "FUT", "OPT", "WRT"];
	private static readonly JsonSerializerSettings _streamJsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly SynchronizedDictionary<long, MarketSubscription> _marketSubscriptions = [];
	private readonly SynchronizedDictionary<string, ShioajiContract> _contracts = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<string, OrderTracker> _orders = new(StringComparer.OrdinalIgnoreCase);
	private readonly SynchronizedDictionary<long, string> _transactionOrders = [];
	private readonly SynchronizedSet<string> _tradeIds = new(StringComparer.OrdinalIgnoreCase);
	private ShioajiRestClient _rest;
	private ShioajiSseClient _sse;
	private ShioajiInfo _info;
	private ShioajiAccount[] _accounts = [];
	private long _orderStatusSubscriptionId;
	private long _portfolioSubscriptionId;
	private string _portfolioFilter;
	private DateTime _lastOrderRefresh;
	private DateTime _lastPortfolioRefresh;

	/// <summary>Supported candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Initializes a new instance of the <see cref="ShioajiMessageAdapter"/>.</summary>
	public ShioajiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(5);
		ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Ticks || dataType.IsTFCandles ||
			dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override IEnumerable<int> SupportedOrderBookDepths { get; } = [5];

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } = ["TWSE", "TPEX", "TAIFEX"];

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage connectMsg, CancellationToken cancellationToken)
	{
		if (_rest != null || _sse != null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);
		if (ReconnectAttempts < 0)
			throw new ArgumentOutOfRangeException(nameof(ReconnectAttempts), ReconnectAttempts,
				"Reconnect attempts cannot be negative.");

		_rest = new(Address, Key, Secret) { Parent = this };
		try
		{
			_info = await _rest.Validate(cancellationToken);
			_accounts = (await _rest.GetAccounts(cancellationToken))?.Where(account => account != null).ToArray() ?? [];
			_sse = new(_rest, ReconnectAttempts) { Parent = this };
			_sse.BeforeConnect += RestoreSubscriptions;
			_sse.EventReceived += OnStreamEvent;
			_sse.Error += SendOutErrorAsync;
			await _sse.Start(cancellationToken);
			await base.ConnectAsync(connectMsg, cancellationToken);
		}
		catch
		{
			await DisposeClients(CancellationToken.None);
			throw;
		}
	}

	/// <inheritdoc />
	protected override async ValueTask DisconnectAsync(DisconnectMessage disconnectMsg,
		CancellationToken cancellationToken)
	{
		if (_rest == null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

		foreach (var request in _marketSubscriptions.Values.Select(subscription => subscription.Request)
			.GroupBy(request => request.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
		{
			try
			{
				await _rest.Unsubscribe(request, cancellationToken);
			}
			catch (ShioajiApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
			{
				this.AddDebugLog("Shioaji market unsubscription was already absent: {0}", ex.Message);
			}
		}

		if (_info?.IsSimulation == false)
		{
			foreach (var account in _accounts.Where(account => account.IsSigned))
			{
				try
				{
					await _rest.UnsubscribeTrade(account, cancellationToken);
				}
				catch (ShioajiApiException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
				{
					this.AddDebugLog("Shioaji trade subscription was already absent: {0}", ex.Message);
				}
			}
		}

		await DisposeClients(cancellationToken);
		await base.DisconnectAsync(disconnectMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage resetMsg, CancellationToken cancellationToken)
	{
		await DisposeClients(cancellationToken);
		_marketSubscriptions.Clear();
		_contracts.Clear();
		_orders.Clear();
		_transactionOrders.Clear();
		_tradeIds.Clear();
		_accounts = [];
		_info = null;
		_orderStatusSubscriptionId = 0;
		_portfolioSubscriptionId = 0;
		_portfolioFilter = null;
		_lastOrderRefresh = default;
		_lastPortfolioRefresh = default;
		await base.ResetAsync(resetMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask TimeAsync(TimeMessage timeMsg, CancellationToken cancellationToken)
	{
		var now = CurrentTime;
		if (_orderStatusSubscriptionId != 0 && now - _lastOrderRefresh >= TimeSpan.FromSeconds(15))
			await SendOrderSnapshot(_orderStatusSubscriptionId, cancellationToken);
		if (_portfolioSubscriptionId != 0 && now - _lastPortfolioRefresh >= TimeSpan.FromSeconds(30))
			await SendPortfolioSnapshot(_portfolioSubscriptionId, _portfolioFilter, cancellationToken);
		await base.TimeAsync(timeMsg, cancellationToken);
	}

	private async ValueTask RestoreSubscriptions(CancellationToken cancellationToken)
	{
		if (_info?.IsSimulation == false)
		{
			foreach (var account in _accounts.Where(account => account.IsSigned))
				await _rest.SubscribeTrade(account, cancellationToken);
		}

		foreach (var request in _marketSubscriptions.Values.Select(subscription => subscription.Request)
			.GroupBy(request => request.Key, StringComparer.OrdinalIgnoreCase).Select(group => group.First()))
			await _rest.Subscribe(request, cancellationToken);
	}

	private async ValueTask OnStreamEvent(string eventName, string json, CancellationToken cancellationToken)
	{
		if (json.IsEmpty())
			return;

		switch (eventName?.ToLowerInvariant())
		{
			case "tick_stk":
			case "tick_fop":
			case "quote_stk":
			case "quote_fop":
			case "bidask_stk":
			case "bidask_fop":
				await ProcessMarketEvent(eventName, Deserialize<ShioajiMarketEvent>(json), cancellationToken);
				break;

			case "quote_idx":
				await ProcessIndexEvent(Deserialize<ShioajiIndexEvent>(json), cancellationToken);
				break;

			case "order_event":
				await ProcessOrderEvent(json, cancellationToken);
				break;

			case "heartbeat":
				_ = Deserialize<ShioajiHeartbeatEvent>(json);
				break;

			default:
				this.AddVerboseLog("Ignored Shioaji SSE event {0}.", eventName);
				break;
		}
	}

	private async ValueTask DisposeClients(CancellationToken cancellationToken)
	{
		if (_sse != null)
		{
			_sse.BeforeConnect -= RestoreSubscriptions;
			_sse.EventReceived -= OnStreamEvent;
			_sse.Error -= SendOutErrorAsync;
			try
			{
				await _sse.Stop(cancellationToken);
			}
			finally
			{
				_sse.Dispose();
				_sse = null;
			}
		}

		_rest?.Dispose();
		_rest = null;
	}

	private async Task<ShioajiContract> ResolveContract(SecurityId securityId, SecurityTypes? securityType,
		CancellationToken cancellationToken)
	{
		if (securityId.Native is string native && !native.IsEmpty())
		{
			if (_contracts.TryGetValue(native, out var nativeContract))
				return nativeContract;
			var parsed = securityId.ParseShioajiContract(securityType);
			CacheContract(parsed);
			return parsed;
		}

		var cacheKey = GetContractCacheKey(securityId.SecurityCode, securityType, securityId.BoardCode);
		if (_contracts.TryGetValue(cacheKey, out var cached))
			return cached;

		var type = securityType?.ToShioajiSecurityType();
		var contract = await _rest.GetContract(securityId.SecurityCode, type, cancellationToken);
		if (contract == null)
			throw new InvalidOperationException($"Shioaji contract '{securityId.SecurityCode}' was not found.");
		CacheContract(contract);
		return contract;
	}

	private void CacheContract(ShioajiContract contract)
	{
		if (contract?.Code.IsEmpty() != false)
			return;
		_contracts[contract.ToNativeKey()] = contract;
		_contracts[GetContractCacheKey(contract.Code, contract.ToSecurityType(), contract.Exchange.ToBoardCode())] = contract;
		_contracts[GetContractCacheKey(contract.Code, contract.ToSecurityType(), null)] = contract;
	}

	private ShioajiAccount GetAccount(string portfolioName, SecurityTypes securityType)
	{
		var accountType = securityType is SecurityTypes.Future or SecurityTypes.Option ? "F" : "S";
		var account = portfolioName.IsEmpty()
			? _accounts.FirstOrDefault(item => item.IsSigned && item.AccountType.EqualsIgnoreCase(accountType))
			: _accounts.FirstOrDefault(item => item.PortfolioName.EqualsIgnoreCase(portfolioName));
		return account ?? throw new InvalidOperationException(LocalizedStrings.AccountNotFound);
	}

	private static string GetContractCacheKey(string code, SecurityTypes? securityType, string boardCode)
		=> $"{code}|{securityType}|{boardCode}";

	private static T Deserialize<T>(string json)
		where T : class
		=> JsonConvert.DeserializeObject<T>(json, _streamJsonSettings)
			?? throw new InvalidDataException($"Shioaji returned an invalid {typeof(T).Name} SSE payload.");
}
