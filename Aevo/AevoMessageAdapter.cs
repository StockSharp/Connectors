namespace StockSharp.Aevo;

/// <summary>The message adapter for Aevo.</summary>
[MediaIcon(Media.MediaNames.aevo)]
[Doc("topics/api/connectors/crypto_exchanges/aevo.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.AevoKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Ticks)]
[OrderCondition(typeof(AevoOrderCondition))]
public partial class AevoMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TickSubscription : MarketSubscription
	{
		public HashSet<string> SeenTrades { get; } = new(StringComparer.Ordinal);
	}

	private sealed class OrderSubscription
	{
		public string Symbol { get; init; }
		public string OrderId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Limit { get; init; }
	}

	private readonly record struct BookLevel(string Price, string Amount,
		string ImpliedVolatility);

	private sealed class OrderBookState
	{
		private readonly SortedDictionary<decimal, BookLevel> _bids =
			new(Comparer<decimal>.Create(static (left, right) =>
				right.CompareTo(left)));
		private readonly SortedDictionary<decimal, BookLevel> _asks = [];

		public bool IsInitialized { get; private set; }

		public DateTime ServerTime { get; private set; }

		public void Apply(AevoOrderBook book)
		{
			ArgumentNullException.ThrowIfNull(book);
			if (book.Type.EqualsIgnoreCase("snapshot"))
			{
				_bids.Clear();
				_asks.Clear();
				IsInitialized = true;
			}
			else if (!book.Type.EqualsIgnoreCase("update"))
				throw new InvalidDataException(
					$"Aevo returned unknown order-book type '{book.Type}'.");
			else if (!IsInitialized)
				throw new InvalidDataException(
					"Aevo sent an order-book update before its snapshot.");

			ApplyLevels(_bids, book.Bids, "bid");
			ApplyLevels(_asks, book.Asks, "ask");
			if (!book.LastUpdated.IsEmpty())
				ServerTime = book.LastUpdated.FromAevoNanoseconds();
			if (!book.Checksum.IsEmpty())
			{
				var expected = uint.TryParse(book.Checksum, NumberStyles.None,
					CultureInfo.InvariantCulture, out var checksum)
					? checksum
					: throw new InvalidDataException(
						$"Aevo returned invalid checksum '{book.Checksum}'.");
				var actual = AevoExtensions.CalculateChecksum(ToWire(_bids, 100),
					ToWire(_asks, 100));
				if (actual != expected)
					throw new InvalidDataException(
						$"Aevo order-book checksum mismatch: {actual} != {expected}.");
			}
		}

		public QuoteChange[] GetBids(int depth) => ToQuotes(_bids, depth);

		public QuoteChange[] GetAsks(int depth) => ToQuotes(_asks, depth);

		private static void ApplyLevels(
			SortedDictionary<decimal, BookLevel> target, string[][] levels,
			string side)
		{
			foreach (var level in levels ?? [])
			{
				if (level is not { Length: >= 2 })
					throw new InvalidDataException(
						$"Aevo returned a malformed {side} level.");
				var price = level[0].ParseAevoDecimal(side + " price");
				var amount = level[1].ParseAevoDecimal(side + " amount");
				if (price <= 0 || amount < 0)
					throw new InvalidDataException(
						$"Aevo returned an invalid {side} level.");
				if (amount == 0)
					target.Remove(price);
				else
					target[price] = new(level[0], level[1],
						level.Length > 2 ? level[2] : null);
			}
		}

		private static QuoteChange[] ToQuotes(
			SortedDictionary<decimal, BookLevel> source, int depth)
			=> [.. source.Take(depth).Select(static pair =>
				new QuoteChange(pair.Key,
					pair.Value.Amount.ParseAevoDecimal("order-book amount")))];

		private static string[][] ToWire(
			SortedDictionary<decimal, BookLevel> source, int depth)
			=> [.. source.Take(depth).Select(static pair => new[]
			{
				pair.Value.Price,
				pair.Value.Amount,
			})];
	}

	private readonly record struct AccountFingerprint(decimal Equity,
		decimal AvailableBalance, decimal AvailableMargin, decimal Balance,
		decimal InitialMargin, decimal MaintenanceMargin, decimal Pnl,
		decimal RealizedPnl, decimal UsedMargin);
	private readonly record struct CollateralFingerprint(decimal Current,
		decimal Blocked, decimal UnrealizedPnl);
	private readonly record struct PositionFingerprint(decimal Current,
		decimal AveragePrice, decimal UnrealizedPnl, decimal LiquidationPrice,
		decimal Leverage, Sides Side);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal Price, decimal Volume, decimal Balance);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, AevoInstrument> _markets =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderBookState> _books =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<string, int> _channelReferences =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions = [];
	private readonly Dictionary<long, AccountFingerprint> _accountFingerprints = [];
	private readonly Dictionary<string, CollateralFingerprint>
		_collateralFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PositionFingerprint> _positionFingerprints =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint> _orderFingerprints =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private AevoAuthenticator _authenticator;
	private AevoRestClient _restClient;
	private AevoSocketClient _socketClient;
	private AevoSigner _signer;
	private string _account;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextPrivatePoll;
	private DateTime _nextPing;

	/// <summary>Initializes a new instance.</summary>
	public AevoMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Aevo];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Aevo) ||
			securityId.IsAssociated(BoardCodes.Aevo);

	private AevoRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private AevoSocketClient SocketClient => _socketClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private AevoSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private string PortfolioName => _portfolioName ?? throw new
		InvalidOperationException("Aevo API credentials are required.");

	private void EnsureConnected()
	{
		if (_restClient is null || _socketClient is null || _signer is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureAccountReady()
	{
		EnsureConnected();
		if (_account.IsEmpty() || _authenticator?.IsAvailable != true)
			throw new InvalidOperationException(
				"Aevo API credentials are required for account data.");
	}

	private void EnsureTradingReady()
	{
		EnsureAccountReady();
		if (!Signer.IsAvailable)
			throw new InvalidOperationException(
				"An Aevo signing key is required for trading.");
	}

	private AevoInstrument GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Aevo.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Aevo instrument '{symbol}'.");
	}

	private AevoInstrument[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _markets.Values];
	}

	private void UpdateServerTime(DateTime time)
	{
		time = time.Kind == DateTimeKind.Utc ? time : time.ToUniversalTime();
		using (_sync.EnterScope())
			if (time > _serverTime)
				_serverTime = time;
	}

	private static bool AddReference(Dictionary<string, int> references,
		string channel)
	{
		if (references.TryGetValue(channel, out var count))
		{
			references[channel] = count + 1;
			return false;
		}
		references[channel] = 1;
		return true;
	}

	private static bool ReleaseReference(Dictionary<string, int> references,
		string channel)
	{
		if (!references.TryGetValue(channel, out var count))
			return false;
		if (count > 1)
		{
			references[channel] = count - 1;
			return false;
		}
		references.Remove(channel);
		return true;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync(default).AsTask().GetAwaiter().GetResult();
		base.DisposeManaged();
	}
}
