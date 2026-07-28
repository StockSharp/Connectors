namespace StockSharp.Settrade;

public partial class SettradeMessageAdapter
{
	private sealed class CandleSubscription
	{
		public SecurityId SecurityId { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _streamGate = new(1, 1);
	private readonly Dictionary<long, SecurityId> _level1Subscriptions = [];
	private readonly Dictionary<long, SecurityId> _depthSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly HashSet<long> _orderSubscriptions = [];
	private readonly Dictionary<string, int> _streamTopics =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _orderTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _tradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private SettradeRestClient _restClient;
	private SettradeMqttClient _mqttClient;
	private DateTime _nextPrivatePoll;
	private DateTime _nextStreamReconnect;

	/// <summary>Supported Settrade candle intervals.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		SettradeExtensions.TimeFrames;

	/// <summary>Initializes a new adapter instance.</summary>
	public SettradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.Set, BoardCodes.Tfex];

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCode) ||
			securityId.IsAssociated(BoardCode);

	private string BoardCode
		=> AccountType == SettradeAccountTypes.Equity
			? BoardCodes.Set
			: BoardCodes.Tfex;

	private SecurityTypes SecurityType
		=> AccountType == SettradeAccountTypes.Equity
			? SecurityTypes.Stock
			: SecurityTypes.Future;

	private SettradeRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private SecurityId ToSecurityId(string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCode,
		};

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private string ResolveAccount(string portfolioName)
	{
		var account = Account.ThrowIfEmpty(nameof(Account));
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(account))
			throw new InvalidOperationException(
				$"Unknown Settrade account '{portfolioName}'.");
		return account;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClientsAsync().AsTask().GetAwaiter().GetResult();
		_streamGate.Dispose();
		base.DisposeManaged();
	}
}
