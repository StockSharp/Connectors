namespace StockSharp.Paxos;

using StockSharp.Paxos.Native;
using StockSharp.Paxos.Native.Model;

/// <summary>The message adapter for Paxos brokerage and custody APIs.</summary>
[MediaIcon(Media.MediaNames.paxos)]
[Doc("topics/api/connectors/crypto_exchanges/paxos.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PaxosKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles)]
[OrderCondition(typeof(PaxosOrderCondition))]
public partial class PaxosMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private enum NativeOperationKinds
	{
		Order,
		Transfer,
		Conversion,
	}

	private sealed class PortfolioReference
	{
		public string Name { get; init; }
		public PaxosProfile Profile { get; init; }
	}

	private sealed class MarketSubscription
	{
		public PaxosMarket Market { get; init; }
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
		public string NativeId { get; init; }
		public string RefId { get; init; }
		public string ProfileId { get; init; }
		public string PortfolioName { get; init; }
		public SecurityId SecurityId { get; init; }
		public long TransactionId { get; init; }
		public NativeOperationKinds Kind { get; init; }
		public PaxosOperations Operation { get; init; }
	}

	private sealed class BookState
	{
		public SortedDictionary<decimal, decimal> Bids { get; } = new(
			Comparer<decimal>.Create(static (left, right) => right.CompareTo(left)));
		public SortedDictionary<decimal, decimal> Asks { get; } = [];
		public bool IsSnapshotLoading { get; set; }
	}

	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct OrderFingerprint(PaxosOrderStatuses Status,
		string Filled, string ModifiedAt);
	private readonly record struct TransferFingerprint(
		PaxosTransferStatuses Status, string UpdatedAt,
		string CryptoTransactionHash);
	private readonly record struct ConversionFingerprint(
		PaxosConversionStatuses Status, string UpdatedAt);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, PaxosMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PortfolioReference> _portfolios =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription> _marketSubscriptions =
		[];
	private readonly Dictionary<long, PortfolioSubscription>
		_portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<string, PaxosSocketClient> _marketSockets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, PaxosSocketClient> _executionSockets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BookState> _books =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOperation> _trackedOperations =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOperation> _trackedReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _nativeIds = [];
	private readonly Dictionary<string, long> _activeOperations =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TransferFingerprint>
		_transferFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, ConversionFingerprint>
		_conversionFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Queue<string> _publicTradeOrder = [];
	private readonly HashSet<string> _seenPrivateExecutions =
		new(StringComparer.OrdinalIgnoreCase);
	private PaxosRestClient _restClient;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public PaxosMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <summary>Time frames provided by the Paxos historical candle API.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(14),
		TimeSpan.FromDays(28),
	];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			dataType == DataType.Transactions ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Paxos];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Paxos) ||
			securityId.IsAssociated(BoardCodes.Paxos);

	private PaxosRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAuthenticated()
	{
		EnsureConnected();
		if (!RestClient.IsAuthenticationAvailable)
			throw new InvalidOperationException(
				"Paxos Client ID and Client Secret are required for private operations.");
	}

	private PaxosMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Paxos.");
		var code = securityId.Native as string;
		if (code.IsEmpty())
			code = securityId.SecurityCode;
		using (_sync.EnterScope())
			return !code.IsEmpty() && _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Paxos market '{code}'.");
	}

	private PaxosMarket[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values.OrderBy(static market => market.Market,
				StringComparer.OrdinalIgnoreCase)];
	}

	private PortfolioReference[] GetPortfolios()
	{
		using (_sync.EnterScope())
			return [.. _portfolios.Values.OrderBy(static portfolio => portfolio.Name,
				StringComparer.OrdinalIgnoreCase)];
	}

	private async ValueTask<PortfolioReference> GetPortfolioAsync(string name,
		CancellationToken cancellationToken)
	{
		name = name.ThrowIfEmpty(nameof(name)).Trim();
		using (_sync.EnterScope())
			if (TryFindPortfolio(name, out var found))
				return found;
		await RefreshProfilesAsync(cancellationToken);
		using (_sync.EnterScope())
			return TryFindPortfolio(name, out var found)
				? found
				: throw new InvalidOperationException(
					$"Unknown Paxos portfolio '{name}'.");
	}

	private bool TryFindPortfolio(string value, out PortfolioReference portfolio)
	{
		if (_portfolios.TryGetValue(value, out portfolio))
			return true;
		portfolio = _portfolios.Values.FirstOrDefault(item =>
			item.Profile.Id.EqualsIgnoreCase(value) ||
			item.Profile.Nickname.EqualsIgnoreCase(value));
		return portfolio is not null;
	}

	private void TrackOperation(TrackedOperation operation, bool isActive)
	{
		if (operation?.NativeId.IsEmpty() != false)
			return;
		using (_sync.EnterScope())
		{
			_trackedOperations[operation.NativeId] = operation;
			if (!operation.RefId.IsEmpty())
				_trackedReferences[operation.RefId] = operation;
			if (operation.TransactionId != 0)
				_nativeIds[operation.TransactionId] = operation.NativeId;
			if (isActive && operation.TransactionId != 0)
				_activeOperations[operation.NativeId] = operation.TransactionId;
			else
				_activeOperations.Remove(operation.NativeId);
		}
	}

	private TrackedOperation GetTrackedOperation(string nativeId, string refId)
	{
		using (_sync.EnterScope())
		{
			if (!nativeId.IsEmpty() &&
				_trackedOperations.TryGetValue(nativeId, out var operation))
				return operation;
			return !refId.IsEmpty() &&
				_trackedReferences.TryGetValue(refId, out operation)
					? operation
					: null;
		}
	}
}
