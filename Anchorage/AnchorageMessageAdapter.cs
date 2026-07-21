namespace StockSharp.Anchorage;

/// <summary>The message adapter for Anchorage Digital.</summary>
[MediaIcon(Media.MediaNames.anchorage)]
[Doc("topics/api/connectors/crypto_exchanges/anchorage.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AnchorageKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth)]
[OrderCondition(typeof(AnchorageOrderCondition))]
public partial class AnchorageMessageAdapter : MessageAdapter
{
	private enum PortfolioKinds
	{
		Trading,
		Vault,
	}

	private enum NativeOperationKinds
	{
		TradingOrder,
		Transfer,
		Transaction,
	}

	private sealed class PortfolioReference
	{
		public string Name { get; init; }
		public string Id { get; init; }
		public string DisplayName { get; init; }
		public PortfolioKinds Kind { get; init; }
	}

	private sealed class MarketSubscription
	{
		public AnchorageTradePair Product { get; init; }
		public DataType DataType { get; init; }
		public int Depth { get; init; }
	}

	private sealed class PortfolioSubscription
	{
		public string PortfolioName { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string NativeId { get; init; }
		public string PortfolioName { get; init; }
		public SecurityId SecurityId { get; init; }
		public Sides? Side { get; init; }
		public decimal? Volume { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private sealed class TrackedOperation
	{
		public string NativeId { get; set; }
		public string ClientOrderId { get; set; }
		public long TransactionId { get; set; }
		public NativeOperationKinds Kind { get; set; }
		public string PortfolioName { get; set; }
		public SecurityId SecurityId { get; set; }
		public AnchorageOperations Operation { get; set; }
	}

	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct TradingOrderFingerprint(
		AnchorageOrderStatuses Status, string CumulativeQuantity,
		string TransactionTime);
	private readonly record struct TransferFingerprint(
		AnchorageTransferStatuses Status, string EndedAt,
		string BlockchainTransactionId);
	private readonly record struct TransactionFingerprint(
		AnchorageTransactionStatuses Status, string Timestamp,
		string BlockchainTransactionId);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, AnchorageTradePair> _products =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AnchorageAssetType> _assets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PortfolioReference> _portfolios =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, AnchorageWallet> _wallets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions =
		[];
	private readonly Dictionary<long, PortfolioSubscription>
		_portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, TrackedOperation> _trackedOperations =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOperation> _clientOperations =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _nativeIds = [];
	private readonly Dictionary<string, long> _activeTradingOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _activeTransfers =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _activeTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TradingOrderFingerprint>
		_tradingOrderFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TransferFingerprint>
		_transferFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TransactionFingerprint>
		_transactionFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private AnchorageRestClient _restClient;
	private AnchorageSocketClient _socketClient;
	private string _marketDataAccountId;
	private DateTime _nextPrivatePoll;
	private DateTime _nextMarketPoll;

	/// <summary>Initializes the adapter.</summary>
	public AnchorageMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			dataType == DataType.Transactions ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Anchorage];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Anchorage) ||
			securityId.IsAssociated(BoardCodes.Anchorage);

	private AnchorageRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private bool IsSocketAvailable => _socketClient is not null;

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private AnchorageTradePair GetProduct(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Anchorage.");
		var code = securityId.Native as string;
		if (code.IsEmpty())
			code = securityId.SecurityCode;
		using (_sync.EnterScope())
			return !code.IsEmpty() && _products.TryGetValue(code, out var product)
				? product
				: throw new InvalidOperationException(
					$"Unknown Anchorage trading pair '{code}'.");
	}

	private AnchorageTradePair GetProduct(string code)
	{
		using (_sync.EnterScope())
			return !code.IsEmpty() && _products.TryGetValue(code, out var product)
				? product
				: null;
	}

	private AnchorageTradePair[] GetProducts()
	{
		using (_sync.EnterScope())
			return [.. _products.Values.Distinct().OrderBy(
				static item => item.Pair, StringComparer.OrdinalIgnoreCase)];
	}

	private PortfolioReference GetPortfolio(string portfolioName)
	{
		portfolioName = portfolioName.ThrowIfEmpty(nameof(portfolioName));
		using (_sync.EnterScope())
			return _portfolios.TryGetValue(portfolioName, out var portfolio)
				? portfolio
				: throw new InvalidOperationException(
					$"Unknown Anchorage portfolio '{portfolioName}'.");
	}

	private PortfolioReference[] GetPortfolios()
	{
		using (_sync.EnterScope())
			return [.. _portfolios.Values.OrderBy(static item => item.Name,
				StringComparer.OrdinalIgnoreCase)];
	}

	private AnchorageWallet GetSourceWallet(PortfolioReference portfolio,
		string assetType, string requestedWalletId)
	{
		if (portfolio.Kind != PortfolioKinds.Vault)
			throw new InvalidOperationException(
				"Anchorage custody operations require a vault portfolio.");
		using (_sync.EnterScope())
		{
			if (!requestedWalletId.IsEmpty())
			{
				if (!_wallets.TryGetValue(requestedWalletId, out var requested) ||
					!requested.VaultId.EqualsIgnoreCase(portfolio.Id) ||
					!HasAsset(requested, assetType))
					throw new InvalidOperationException(
						$"Anchorage wallet '{requestedWalletId}' does not hold " +
						$"{assetType} in {portfolio.Name}.");
				return requested;
			}
			var matches = _wallets.Values.Where(wallet => !wallet.IsArchived &&
				wallet.VaultId.EqualsIgnoreCase(portfolio.Id) &&
				HasAsset(wallet, assetType)).ToArray();
			if (matches.Length == 1)
				return matches[0];
			var defaults = matches.Where(static wallet => wallet.IsDefault).ToArray();
			if (defaults.Length == 1)
				return defaults[0];
			if (matches.Length == 0)
				throw new InvalidOperationException(
					$"No Anchorage wallet holds {assetType} in {portfolio.Name}.");
			throw new InvalidOperationException(
				$"Multiple Anchorage wallets hold {assetType} in {portfolio.Name}; " +
				"set SourceWalletId explicitly.");
		}
	}

	private static bool HasAsset(AnchorageWallet wallet, string assetType)
		=> (wallet.Assets ?? []).Any(asset =>
			asset?.AssetType.EqualsIgnoreCase(assetType) == true);

	private void UpdateReferenceData(IEnumerable<AnchorageTradePair> products,
		IEnumerable<AnchorageAssetType> assets,
		IEnumerable<AnchorageTradingAccount> accounts,
		IEnumerable<AnchorageVault> vaults, IEnumerable<AnchorageWallet> wallets)
	{
		using (_sync.EnterScope())
		{
			_products.Clear();
			foreach (var product in products.Where(static item =>
				item is not null && !item.Pair.IsEmpty()))
				_products[product.Pair] = product;
			_assets.Clear();
			foreach (var asset in assets.Where(static item =>
				item is not null && !item.AssetType.IsEmpty()))
				_assets.TryAdd(asset.AssetType, asset);
			_portfolios.Clear();
			foreach (var account in accounts.Where(static item =>
				item is not null && !item.Id.IsEmpty()))
			{
				var name = AnchorageExtensions.GetTradingPortfolioName(account.Id);
				_portfolios[name] = new()
				{
					Name = name,
					Id = account.Id,
					DisplayName = account.Name,
					Kind = PortfolioKinds.Trading,
				};
			}
			foreach (var vault in vaults.Where(static item =>
				item is not null && !item.Id.IsEmpty()))
			{
				var name = AnchorageExtensions.GetVaultPortfolioName(vault.Id);
				_portfolios[name] = new()
				{
					Name = name,
					Id = vault.Id,
					DisplayName = vault.Name,
					Kind = PortfolioKinds.Vault,
				};
			}
			_wallets.Clear();
			foreach (var wallet in wallets.Where(static item =>
				item is not null && !item.Id.IsEmpty()))
				_wallets[wallet.Id] = wallet;
		}
	}

	private void TrackOperation(TrackedOperation operation)
	{
		if (operation?.NativeId.IsEmpty() != false)
			return;
		using (_sync.EnterScope())
		{
			_trackedOperations[operation.NativeId] = operation;
			if (!operation.ClientOrderId.IsEmpty())
				_clientOperations[operation.ClientOrderId] = operation;
			if (operation.TransactionId != 0)
				_nativeIds[operation.TransactionId] = operation.NativeId;
		}
	}

	private TrackedOperation GetTrackedOperation(string nativeId,
		string clientOrderId)
	{
		using (_sync.EnterScope())
		{
			TrackedOperation operation = null;
			if (!nativeId.IsEmpty())
				_trackedOperations.TryGetValue(nativeId, out operation);
			if (operation is null && !clientOrderId.IsEmpty())
				_clientOperations.TryGetValue(clientOrderId, out operation);
			return operation;
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
