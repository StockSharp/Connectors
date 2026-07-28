namespace StockSharp.Coincall;

public partial class CoincallMessageAdapter
{
	private const int _maximumRememberedTradeIds = 10000;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(
		string Channel,
		string Symbol,
		string Period);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, CoincallInstrument>
		_instruments = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription>
		_depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription>
		_tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int>
		_streamReferences = [];
	private readonly Dictionary<long, long>
		_orderTransactions = [];
	private readonly HashSet<string> _seenTradeIds =
		new(StringComparer.Ordinal);
	private readonly Queue<string> _seenTradeOrder = [];
	private readonly SemaphoreSlim _pollSync = new(1, 1);
	private CoincallRestClient _restClient;
	private CoincallWsClient _wsClient;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private int _privateSubscriptionReferences;
	private DateTime _lastPrivatePoll;
	private DateTime _lastHeartbeat;

	/// <summary>
	/// Initializes a new instance of the
	/// <see cref="CoincallMessageAdapter"/>.
	/// </summary>
	public CoincallMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(
		MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override bool IsSupportTransactionLog => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards =>
	[
		BoardCodes.CoincallOptions,
		BoardCodes.CoincallFutures,
	];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(
				ProductType.ToBoardCode()) ||
			securityId.IsAssociated(ProductType.ToBoardCode());

	private CoincallRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(
			LocalizedStrings.ConnectionNotOk);

	private CoincallWsClient WsClient
		=> _wsClient ?? throw new InvalidOperationException(
			"Coincall streaming requires an API key and secret.");

	private void EnsureConnected()
	{
		if (_restClient is null)
			throw new InvalidOperationException(
				LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Coincall API key and secret are required for " +
					"private operations.");
	}

	private CoincallInstrument GetInstrument(SecurityId securityId)
	{
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _instruments.TryGetValue(code, out var instrument)
				? instrument
				: throw new InvalidOperationException(
					$"Unknown Coincall instrument '{code}'.");
	}

	private CoincallInstrument GetInstrument(string symbol)
	{
		if (symbol.IsEmpty())
			return null;
		using (_sync.EnterScope())
			return _instruments.TryGetValue(
				symbol.Trim(), out var instrument)
					? instrument
					: null;
	}

	private CoincallInstrument[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private void RegisterInstruments(
		IEnumerable<CoincallInstrument> instruments)
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			foreach (var instrument in instruments ?? [])
			{
				if (instrument?.Symbol.IsEmpty() != false)
					continue;
				instrument.Symbol =
					instrument.Symbol.Trim().ToUpperInvariant();
				_instruments[instrument.Symbol] = instrument;
			}
		}
	}

	private static bool AddReference(
		IDictionary<StreamKey, int> references,
		StreamKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references[key] = 1;
		return true;
	}

	private static bool ReleaseReference(
		IDictionary<StreamKey, int> references,
		StreamKey key)
	{
		if (!references.TryGetValue(key, out var count))
			return false;
		if (count > 1)
		{
			references[key] = count - 1;
			return false;
		}
		references.Remove(key);
		return true;
	}

	private bool AddTrade(string symbol, string tradeId)
	{
		if (symbol.IsEmpty() || tradeId.IsEmpty())
			return false;
		var key = symbol + ":" + tradeId;
		using (_sync.EnterScope())
		{
			if (!_seenTradeIds.Add(key))
				return false;
			_seenTradeOrder.Enqueue(key);
			while (_seenTradeOrder.Count >
				_maximumRememberedTradeIds)
				_seenTradeIds.Remove(_seenTradeOrder.Dequeue());
		}
		return true;
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_instruments.Clear();
			_level1Subscriptions.Clear();
			_depthSubscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_streamReferences.Clear();
			_orderTransactions.Clear();
			_seenTradeIds.Clear();
			_seenTradeOrder.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_privateSubscriptionReferences = 0;
		_lastPrivatePoll = default;
		_lastHeartbeat = default;
	}

	private string GetPortfolioName()
		=> $"Coincall {ProductType}";

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		DisposeClients();
		_pollSync.Dispose();
		base.DisposeManaged();
	}
}
