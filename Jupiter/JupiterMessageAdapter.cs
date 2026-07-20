namespace StockSharp.Jupiter;

public partial class JupiterMessageAdapter
{
	private sealed class Level1Subscription
	{
		public JupiterMarket Market { get; init; }
	}

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public JupiterMarket Market { get; init; }
		public JupiterTrackedOrderKinds Kind { get; init; }
		public string OrderId { get; set; }
		public string Signature { get; set; }
		public string PositionId { get; set; }
		public Sides Side { get; init; }
		public decimal Volume { get; set; }
		public decimal Price { get; set; }
		public decimal? Commission { get; set; }
		public string CommissionCurrency { get; set; }
		public DateTime SubmittedTime { get; init; }
		public OrderStates State { get; set; }
		public bool IsTradeSent { get; set; }
	}

	private sealed class OrderSubscription
	{
		public string OrderId { get; init; }
		public SecurityId SecurityId { get; init; }
		public Sides? Side { get; init; }
		public OrderStates[] States { get; init; }
		public DateTime? From { get; init; }
		public DateTime? To { get; init; }
		public int Skip { get; init; }
		public int Maximum { get; init; }
	}

	private readonly record struct Level1Fingerprint(decimal Bid,
		decimal Ask, decimal Last, decimal High, decimal Low, decimal Volume,
		decimal Change);
	private readonly record struct BalanceFingerprint(decimal Current,
		decimal Blocked);
	private readonly record struct PositionFingerprint(decimal Current,
		decimal Average, decimal Mark, decimal Pnl, decimal Liquidation,
		decimal Leverage, Sides Side);
	private readonly record struct OrderFingerprint(OrderStates State,
		decimal Price, decimal Volume, bool IsTradeSent);
	private readonly record struct TokenBalance(string Code, string Identity,
		decimal Current, decimal Blocked);
	private sealed class PrivateSnapshot
	{
		public JupiterHoldingsResponse Holdings { get; init; }
		public JupiterPerpetualPosition[] Positions { get; init; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, JupiterMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, JupiterToken> _tokens =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly Dictionary<string, Level1Fingerprint>
		_level1Fingerprints = new(StringComparer.Ordinal);
	private readonly HashSet<long> _portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription>
		_orderSubscriptions = [];
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, PositionFingerprint>
		_positionFingerprints = new(StringComparer.Ordinal);
	private readonly Dictionary<string, OrderFingerprint>
		_orderFingerprints = new(StringComparer.Ordinal);
	private JupiterApiClient _apiClient;
	private JupiterSigner _signer;
	private DateTime _nextMarketPoll;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public JupiterMessageAdapter(IdGenerator transactionIdGenerator)
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
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Jupiter];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Jupiter) ||
			securityId.IsAssociated(BoardCodes.Jupiter);

	private JupiterApiClient ApiClient => _apiClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private JupiterSigner Signer => _signer ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_apiClient is null || _signer is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureTradingReady()
	{
		EnsureConnected();
		if (!Signer.IsSigningAvailable)
			throw new InvalidOperationException(
				"A Solana private key is required for Jupiter trading.");
	}

	private JupiterMarket GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not Jupiter.");
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId))
			.Trim().ToUpperInvariant();
		using (_sync.EnterScope())
			return _markets.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Jupiter market '{code}'.");
	}

	private string GetPortfolioName()
	{
		if (!Signer.IsWalletAvailable)
			throw new InvalidOperationException(
				"A Solana wallet address is required for portfolio data.");
		return $"Jupiter_{Signer.WalletAddress[..8]}";
	}

	private void ValidatePortfolio(string portfolioName)
	{
		if (!portfolioName.IsEmpty() &&
			!portfolioName.EqualsIgnoreCase(GetPortfolioName()))
			throw new InvalidOperationException(
				$"Unknown Jupiter portfolio '{portfolioName}'.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		base.DisposeManaged();
	}
}
