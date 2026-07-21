namespace StockSharp.Fireblocks;

public partial class FireblocksMessageAdapter
{
	private sealed class PortfolioSubscription
	{
		public string PortfolioName { get; init; }
	}

	private sealed class OrderSubscription
	{
		public string FireblocksId { get; init; }
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
	private readonly record struct TransactionFingerprint(
		FireblocksTransactionStatuses Status, decimal? LastUpdated,
		string SubStatus, string TransactionHash);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, FireblocksVaultAccount> _vaults =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, PortfolioSubscription>
		_portfolioSubscriptions = [];
	private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
		[];
	private readonly Dictionary<string, long> _localTransactionIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, string> _fireblocksTransactionIds = [];
	private readonly Dictionary<string, long> _activeTransactions =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BalanceFingerprint>
		_balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TransactionFingerprint>
		_transactionFingerprints = new(StringComparer.OrdinalIgnoreCase);
	private FireblocksRestClient _restClient;
	private DateTime _nextPrivatePoll;

	/// <summary>Initializes the adapter.</summary>
	public FireblocksMessageAdapter(IdGenerator transactionIdGenerator)
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
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Fireblocks];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Fireblocks) ||
			securityId.IsAssociated(BoardCodes.Fireblocks);

	private FireblocksRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private FireblocksVaultAccount GetVault(string portfolioName)
	{
		portfolioName = portfolioName.ThrowIfEmpty(nameof(portfolioName));
		using (_sync.EnterScope())
			return _vaults.TryGetValue(portfolioName, out var vault)
				? vault
				: throw new InvalidOperationException(
					$"Unknown Fireblocks portfolio '{portfolioName}'.");
	}

	private void UpdateVaults(IEnumerable<FireblocksVaultAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);
		using (_sync.EnterScope())
		{
			_vaults.Clear();
			foreach (var account in accounts.Where(static account =>
				account is not null && !account.Id.IsEmpty()))
				_vaults[account.GetPortfolioName()] = account;
		}
	}

	private bool IsKnownVaultAsset(string assetId)
	{
		using (_sync.EnterScope())
			return _vaults.Values.Any(account => (account.Assets ?? [])
				.Any(asset => asset.Id.EqualsIgnoreCase(assetId)));
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
