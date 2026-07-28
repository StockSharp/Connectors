namespace StockSharp.SSI;

public partial class SSIMessageAdapter
{
	private sealed class CandleSubscription
	{
		public SecurityId SecurityId { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _streamGate = new(1, 1);
	private readonly Dictionary<long, SecurityId> _level1Subscriptions =
		[];
	private readonly Dictionary<long, SecurityId> _depthSubscriptions =
		[];
	private readonly Dictionary<long, SecurityId> _tickSubscriptions =
		[];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<long, string[]> _portfolioSubscriptions =
		[];
	private readonly HashSet<long> _orderSubscriptions = [];
	private readonly Dictionary<string, int> _streamTopics =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _securityBoards =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, SecurityTypes> _securityTypes =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _matchIds =
		new(StringComparer.OrdinalIgnoreCase);
	private SSIRestClient _restClient;
	private SSIWebSocketClient _streamClient;
	private DateTimeOffset _nextPrivatePoll;
	private DateTimeOffset _nextStreamReconnect;

	/// <summary>Supported SSI candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		SSIExtensions.TimeFrames;

	/// <summary>Initializes a new adapter instance.</summary>
	public SSIMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.Hose, BoardCodes.Hnx, BoardCodes.Upcom];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			AssociatedBoards.Any(board =>
				securityId.IsAssociated(board));

	private SSIRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private SecurityId Normalize(SecurityId securityId)
	{
		var symbol = securityId.SecurityCode
			.ThrowIfEmpty(nameof(securityId)).Trim().ToUpperInvariant();
		var board = securityId.BoardCode;
		if (!board.IsEmpty() &&
			!AssociatedBoards.Any(board.EqualsIgnoreCase))
			throw new InvalidOperationException(
				$"Security board '{board}' is not associated with SSI.");
		if (board.IsEmpty())
			using (_sync.EnterScope())
				_securityBoards.TryGetValue(symbol, out board);
		return new()
		{
			SecurityCode = symbol,
			BoardCode = board.IsEmpty() ? BoardCodes.Hose : board,
		};
	}

	private SecurityId ToSecurityId(string symbol)
		=> Normalize(new() { SecurityCode = symbol });

	private SecurityTypes ToSecurityType(string symbol)
	{
		using (_sync.EnterScope())
			return _securityTypes.TryGetValue(symbol, out var value)
				? value
				: symbol.StartsWith("VN30F",
					StringComparison.OrdinalIgnoreCase)
					? SecurityTypes.Future
					: SecurityTypes.Stock;
	}

	private string ResolveAccount(string account)
	{
		var configured = Account.ThrowIfEmpty(nameof(Account));
		if (!account.IsEmpty() &&
			!account.EqualsIgnoreCase(configured))
			throw new InvalidOperationException(
				$"Unknown SSI account '{account}'.");
		return configured;
	}

	private static string TopicChannel(string topic)
		=> topic.StartsWith("order.",
				StringComparison.OrdinalIgnoreCase) ||
			topic.StartsWith("portfolio.",
				StringComparison.OrdinalIgnoreCase)
			? "TRADING"
			: "DATA";

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync().AsTask().GetAwaiter().GetResult();
		_streamGate.Dispose();
		base.DisposeManaged();
	}
}
