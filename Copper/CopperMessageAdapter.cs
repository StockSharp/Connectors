namespace StockSharp.Copper;

/// <summary>The message adapter for Copper custody and ClearLoop.</summary>
[MediaIcon(Media.MediaNames.copper)]
[Doc("topics/api/connectors/crypto_exchanges/copper.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.CopperKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.History | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(CopperOrderCondition))]
public partial class CopperMessageAdapter : MessageAdapter
{
	private sealed class PortfolioReference
	{
		public string Name { get; init; }
		public string PortfolioId { get; init; }
		public string PortfolioName { get; init; }
		public string ClientAccountId { get; init; }
		public bool IsClearLoop { get; init; }
	}

	private sealed class PortfolioSubscription
	{
		public string PortfolioName { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string CopperId { get; init; }
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

	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(CopperOrderStatuses Status,
		string UpdatedAt, string TransactionId);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, PortfolioReference> _portfolios =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, CopperCurrency> _currencies =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, PortfolioSubscription>
		_portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, long> _localTransactionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _copperOrderIds = [];
	private readonly Dictionary<string, long> _activeOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private CopperRestClient _restClient;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public CopperMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
		this.AddSupportedMessage(MessageTypes.SecurityLookup, true);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			dataType == DataType.Transactions ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Copper];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Copper) ||
			securityId.IsAssociated(BoardCodes.Copper);

	private CopperRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private PortfolioReference GetPortfolio(string portfolioName)
	{
		portfolioName = portfolioName.ThrowIfEmpty(nameof(portfolioName));
		using (_sync.EnterScope())
			return _portfolios.TryGetValue(portfolioName, out var portfolio)
				? portfolio
				: throw new InvalidOperationException(
					$"Unknown Copper portfolio '{portfolioName}'.");
	}

	private PortfolioReference[] GetPortfolios()
	{
		using (_sync.EnterScope())
			return [.. _portfolios.Values.OrderBy(static item => item.Name,
				StringComparer.OrdinalIgnoreCase)];
	}

	private void UpdatePortfolios(IEnumerable<CopperPortfolio> portfolios,
		IEnumerable<CopperClearLoopPortfolio> clearLoopPortfolios)
	{
		ArgumentNullException.ThrowIfNull(portfolios);
		ArgumentNullException.ThrowIfNull(clearLoopPortfolios);
		using (_sync.EnterScope())
		{
			_portfolios.Clear();
			foreach (var portfolio in portfolios.Where(static item =>
				item is not null && !item.Id.IsEmpty()))
			{
				var name = CopperExtensions.GetPortfolioName(portfolio.Id);
				_portfolios[name] = new()
				{
					Name = name,
					PortfolioId = portfolio.Id,
					PortfolioName = portfolio.Name,
					IsClearLoop = portfolio.Type == CopperPortfolioTypes.ClearLoop,
				};
			}
			foreach (var portfolio in clearLoopPortfolios.Where(static item =>
				item is not null && !item.PortfolioId.IsEmpty() &&
				!item.ClientAccountId.IsEmpty()))
			{
				var name = CopperExtensions.GetClearLoopPortfolioName(
					portfolio.PortfolioId, portfolio.ClientAccountId);
				_portfolios[name] = new()
				{
					Name = name,
					PortfolioId = portfolio.PortfolioId,
					PortfolioName = "ClearLoop " + portfolio.ClientAccountId,
					ClientAccountId = portfolio.ClientAccountId,
					IsClearLoop = true,
				};
			}
		}
	}

	private void UpdateCurrencies(IEnumerable<CopperCurrency> currencies)
	{
		ArgumentNullException.ThrowIfNull(currencies);
		using (_sync.EnterScope())
		{
			_currencies.Clear();
			foreach (var currency in currencies.Where(static item =>
				item is not null && !item.Currency.IsEmpty()))
				_currencies.TryAdd(currency.Currency, currency);
		}
	}

	private bool IsKnownCurrency(string currency)
	{
		using (_sync.EnterScope())
			return !currency.IsEmpty() && _currencies.ContainsKey(currency);
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
