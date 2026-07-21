namespace StockSharp.GainsNetwork;

/// <summary>The message adapter for Gains Network gTrade.</summary>
[MediaIcon(Media.MediaNames.gainsnetwork)]
[Doc("topics/api/connectors/crypto_exchanges/gains_network.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GainsNetworkKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1)]
[OrderCondition(typeof(GainsNetworkOrderCondition))]
public partial class GainsNetworkMessageAdapter : MessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, GainsMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<int, GainsMarket> _marketsByIndex = [];
	private readonly Dictionary<int, GainsMarketPrice> _prices = [];
	private readonly Dictionary<long, int> _level1Subscriptions = [];
	private readonly Dictionary<int, long> _transactionByOrder = [];
	private readonly Dictionary<int, GainsTradeContainer> _knownOrders = [];
	private readonly HashSet<string> _knownPositions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<long> _seenHistory = [];
	private GainsNetworkDeployment _deployment;
	private GainsTradingVariables _variables;
	private GainsNetworkApiClient _apiClient;
	private GainsNetworkRpcClient _rpcClient;
	private GainsNetworkSocketClient _socketClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private DateTime _serverTime;
	private DateTime _nextAccountRefresh;
	private DateTime _nextMarketRefresh;

	/// <summary>Initializes a new instance.</summary>
	public GainsNetworkMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.GainsNetwork];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.GainsNetwork) ||
			securityId.IsAssociated(BoardCodes.GainsNetwork);

	private GainsNetworkApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GainsNetworkRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GainsNetworkSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GainsTradingVariables Variables => _variables ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.EnsureUtc();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private GainsMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				"Security board '" + securityId.BoardCode +
				"' is not Gains Network.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					"Unknown Gains market '" + symbol + "'.");
	}

	private GainsMarket GetMarket(int pairIndex)
	{
		using (_sync.EnterScope())
			return _marketsByIndex.TryGetValue(pairIndex, out var market)
				? market
				: null;
	}

	private GainsMarket GetMarket(string pair)
	{
		if (pair.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _markets.TryGetValue(pair.Trim(), out var market)
				? market
				: null;
	}

	private GainsMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsByIndex.Values.OrderBy(static market =>
				market.PairIndex)];
	}

	private GainsMarketPrice GetPrice(int pairIndex)
	{
		using (_sync.EnterScope())
			return _prices.TryGetValue(pairIndex, out var price) ? price : null;
	}

	private GainsCollateral GetCollateral(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		var collateral = (Variables.Collaterals ?? []).FirstOrDefault(item =>
			item is not null && item.IsActive &&
			string.Equals(item.Symbol, symbol,
				StringComparison.OrdinalIgnoreCase));
		return collateral ?? throw new InvalidOperationException(
			"Gains collateral '" + symbol + "' is not active on " +
			_deployment.Name + ".");
	}

	private GainsCollateral GetCollateral(int collateralIndex)
		=> (Variables.Collaterals ?? []).FirstOrDefault(item => item is not null &&
			item.CollateralIndex == collateralIndex) ??
			throw new InvalidDataException("Gains references unknown collateral " +
				collateralIndex + ".");

	private void EnsureConnected()
	{
		if (_apiClient is null || _rpcClient is null || _socketClient is null ||
			_variables is null || _deployment is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (!RpcClient.IsWalletConfigured)
			throw new InvalidOperationException(
				"A Gains EVM wallet address is required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!RpcClient.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Gains EVM private key is required for transactions.");
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() && !portfolioName.Equals(_portfolioName,
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException(
				"Unknown Gains portfolio '" + portfolioName + "'.");
	}

	private long GetOriginalTransactionId(int orderIndex)
	{
		using (_sync.EnterScope())
			return _transactionByOrder.TryGetValue(orderIndex, out var id)
				? id
				: _orderStatusSubscriptionId;
	}

	private void TrackOrder(int orderIndex, long transactionId)
	{
		if (orderIndex < 0 || transactionId == 0)
			return;
		using (_sync.EnterScope())
			_transactionByOrder[orderIndex] = transactionId;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_markets.Clear();
			_marketsByIndex.Clear();
			_prices.Clear();
			_level1Subscriptions.Clear();
			_transactionByOrder.Clear();
			_knownOrders.Clear();
			_knownPositions.Clear();
			_seenHistory.Clear();
			_serverTime = default;
			_nextAccountRefresh = default;
			_nextMarketRefresh = default;
		}
		_variables = null;
		_deployment = null;
		_portfolioName = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
