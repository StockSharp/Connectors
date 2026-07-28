namespace StockSharp.DexScreener;

public partial class DexScreenerMessageAdapter
{
	private sealed class Level1Subscription
	{
		public DexScreenerPair Pair { get; init; }
		public DateTime LastUpdate { get; set; }
	}

	private readonly Lock _sync = new();
	private readonly Dictionary<string, DexScreenerPair>
		_pairsByNative =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, DexScreenerPair>
		_pairsByCode =
			new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, Level1Subscription>
		_level1Subscriptions = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private DexScreenerRestClient _restClient;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="DexScreenerMessageAdapter"/>.
	/// </summary>
	public DexScreenerMessageAdapter(
		IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.RemoveTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards { get; } =
		[BoardCodes.DexScreener];

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities;

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.IsAssociated(BoardCodes.DexScreener);

	private DexScreenerRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void RememberPairs(
		IEnumerable<DexScreenerPair> pairs)
	{
		using (_sync.EnterScope())
		{
			foreach (var pair in pairs ?? [])
			{
				if (pair?.NativeId.IsEmpty() != false ||
					pair.Symbol.IsEmpty())
					continue;
				_pairsByNative[pair.NativeId] = pair;
				_pairsByCode[pair.Symbol] = pair;
			}
		}
	}

	private DexScreenerPair GetPair(SecurityId securityId)
	{
		using (_sync.EnterScope())
		{
			if (securityId.Native is string native &&
				!native.IsEmpty() &&
				_pairsByNative.TryGetValue(native, out var pair))
				return pair;
			if (!securityId.SecurityCode.IsEmpty() &&
				_pairsByCode.TryGetValue(
					securityId.SecurityCode, out pair))
				return pair;
		}
		throw new InvalidOperationException(
			$"Unknown DEX Screener pool " +
				$"'{securityId.SecurityCode}'. Run security lookup first.");
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_pairsByNative.Clear();
			_pairsByCode.Clear();
			_level1Subscriptions.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_restClient?.Dispose();
		_restClient = null;
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
