namespace StockSharp.BitpandaFusion;

public partial class BitpandaFusionMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, BitpandaFusionPair> _pairs =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BitpandaFusionAsset> _assets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, TrackedOrder> _trackedOrders =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private BitpandaFusionRestClient _restClient;

	private sealed class TrackedOrder
	{
		public long TransactionId { get; init; }
		public string Pair { get; init; }
		public Sides Side { get; init; }
		public OrderTypes OrderType { get; init; }
		public decimal Volume { get; init; }
		public decimal Price { get; init; }
		public decimal? TriggerPrice { get; init; }
		public TimeInForce? TimeInForce { get; init; }
		public DateTime? TillDate { get; init; }
	}

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="BitpandaFusionMessageAdapter"/> class.
	/// </summary>
	public BitpandaFusionMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
		this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> false;

	/// <inheritdoc />
	public override bool IsReplaceCommandEditCurrent => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.BitpandaFusion];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitpandaFusion) ||
			securityId.IsAssociated(BoardCodes.BitpandaFusion);

	private BitpandaFusionRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private string GetPair(SecurityId securityId)
	{
		var pair = NormalizePair(securityId.SecurityCode);
		using (_sync.EnterScope())
			if (_pairs.ContainsKey(pair))
				return pair;
		throw new InvalidOperationException(
			$"Unknown Bitpanda Fusion pair '{pair}'.");
	}

	private static string NormalizePair(string pair)
	{
		pair = pair.ThrowIfEmpty(nameof(pair)).Trim().ToUpperInvariant()
			.Replace('_', '-').Replace('/', '-');
		while (pair.Contains("--", StringComparison.Ordinal))
			pair = pair.Replace("--", "-", StringComparison.Ordinal);
		return pair;
	}

	private string GetPortfolioName()
		=> $"BitpandaFusion_{Token.ToId()}";

	private void RegisterReferenceData(IEnumerable<BitpandaFusionPair> pairs,
		IEnumerable<BitpandaFusionAsset> assets)
	{
		using (_sync.EnterScope())
		{
			_pairs.Clear();
			foreach (var pair in pairs ?? [])
				if (!pair.Pair.IsEmpty())
					_pairs[NormalizePair(pair.Pair)] = pair;
			_assets.Clear();
			foreach (var asset in assets ?? [])
				if (!asset.Symbol.IsEmpty())
					_assets[asset.Symbol.ToUpperInvariant()] = asset;
		}
	}

	private void TrackOrder(string id, TrackedOrder order)
	{
		if (id.IsEmpty() || order is null)
			return;
		using (_sync.EnterScope())
			_trackedOrders[id] = order;
	}

	private TrackedOrder GetTrackedOrder(string id)
	{
		if (id.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _trackedOrders.TryGetValue(id, out var order) ? order : null;
	}

	private bool AddTradeId(string id)
	{
		if (id.IsEmpty())
			return false;
		using (_sync.EnterScope())
			return _seenTradeIds.Add(id);
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_pairs.Clear();
			_assets.Clear();
			_trackedOrders.Clear();
			_seenTradeIds.Clear();
		}
	}

	private static string ResolveOrderId(long? numericId, string stringId)
	{
		if (!stringId.IsEmpty())
			return stringId;
		if (numericId is > 0)
			return numericId.Value.ToString(CultureInfo.InvariantCulture);
		throw new InvalidOperationException(
			"Bitpanda Fusion order identifier is required.");
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClient();
		base.DisposeManaged();
	}
}
