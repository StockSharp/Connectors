namespace StockSharp.Backpack;

public partial class BackpackMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _depthProcessing = new(1, 1);
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<string, int> _streamReferences =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BackpackMarket> _markets =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private BackpackRestClient _restClient;
	private BackpackWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public bool IsPerpetual { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
		public bool IsSnapshotReady { get; set; }
		public long LastUpdateId { get; set; }
		public List<BackpackWsDepth> Pending { get; } = [];
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="BackpackMessageAdapter"/>.
	/// </summary>
	public BackpackMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
		[BoardCodes.Backpack, BoardCodes.BackpackFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Backpack)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BackpackFutures)
			|| securityId.IsAssociated(BoardCodes.Backpack)
			|| securityId.IsAssociated(BoardCodes.BackpackFutures);

	private BackpackRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private BackpackWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			"Backpack Exchange WebSocket is not connected.");

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureRealtimeReady()
	{
		EnsureConnected();
		if (_wsClient is null)
			throw new InvalidOperationException(
				"Backpack Exchange WebSocket is unavailable for this connection.");
	}

	private void EnsurePrivateReady()
	{
		EnsureRealtimeReady();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Backpack Exchange public and private API keys are required for private operations.");
	}

	private string GetSymbol(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Trim().Replace('-', '_').Replace('/', '_').ToUpperInvariant();
		using (_sync.EnterScope())
		{
			if (_markets.ContainsKey(code))
				return code;
			if (securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BackpackFutures))
			{
				var perpetual = code.EndsWith("_PERP", StringComparison.Ordinal)
					? code
					: code + "_PERP";
				if (_markets.ContainsKey(perpetual))
					return perpetual;
			}
			var compact = code.Replace("_", string.Empty, StringComparison.Ordinal);
			var candidates = _markets.Keys
				.Where(symbol => symbol.Replace("_", string.Empty,
					StringComparison.Ordinal).EqualsIgnoreCase(compact))
				.Take(2).ToArray();
			if (candidates.Length == 1)
				return candidates[0];
		}
		throw new InvalidOperationException(
			$"Unknown Backpack Exchange market '{securityId.SecurityCode}'.");
	}

	private BackpackMarket GetMarket(string symbol)
	{
		using (_sync.EnterScope())
			return _markets.TryGetValue(symbol, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown Backpack Exchange market '{symbol}'.");
	}

	private static bool IsPerpetual(BackpackMarket market)
		=> market.MarketType is BackpackMarketTypes.Perpetual or
			BackpackMarketTypes.InversePerpetual or BackpackMarketTypes.Dated;

	private static string GetPortfolioName(SecureString key)
		=> $"Backpack_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private string GetPortfolioName() => GetPortfolioName(Key);

	private static uint CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && uint.TryParse(userOrderId, NumberStyles.None,
			CultureInfo.InvariantCulture, out var userId))
			return userId;
		if (transactionId is < 0 or > uint.MaxValue)
			throw new InvalidOperationException(
				"Backpack Exchange requires a client order ID between 0 and 4294967295.");
		return (uint)transactionId;
	}

	private static int NormalizeDepth(int? requested)
	{
		var depth = (requested ?? 100).Min(1000).Max(1);
		return new[] { 5, 10, 20, 50, 100, 500, 1000 }
			.First(value => value >= depth);
	}

	private static bool AddReference(IDictionary<string, int> references, string stream)
	{
		if (references.TryGetValue(stream, out var count))
		{
			references[stream] = count + 1;
			return false;
		}
		references.Add(stream, 1);
		return true;
	}

	private static bool ReleaseReference(IDictionary<string, int> references, string stream)
	{
		if (!references.TryGetValue(stream, out var count))
			return false;
		if (count > 1)
		{
			references[stream] = count - 1;
			return false;
		}
		references.Remove(stream);
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_markets.Clear();
			_seenFillIds.Clear();
		}
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_depthProcessing.Dispose();
		base.DisposeManaged();
	}
}
