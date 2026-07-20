namespace StockSharp.StandX;

public partial class StandXMessageAdapter
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

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	private readonly record struct StreamKey(
		StandXChannels Channel, string Symbol);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, StandXSymbolInfo> _instruments =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, long> _transactionByClientOrderId =
		new(StringComparer.Ordinal);
	private readonly HashSet<long> _seenUserTradeIds = [];
	private StandXRestClient _restClient;
	private StandXMarketWebSocketClient _marketSocket;
	private StandXOrderWebSocketClient _orderSocket;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private string _portfolioName;
	private DateTime _nextTimeSynchronization;
	private DateTime _nextCandlePoll;

	/// <summary>
	/// Initializes a new instance of the <see cref="StandXMessageAdapter"/>.
	/// </summary>
	public StandXMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(StandXExtensions.TimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities ||
			dataType == DataType.PositionChanges ||
			base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription)
		=> true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.StandX];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.StandX) ||
			securityId.IsAssociated(BoardCodes.StandX);

	private StandXRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private StandXMarketWebSocketClient MarketSocket => _marketSocket ??
		throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private StandXOrderWebSocketClient OrderSocket => _orderSocket ??
		throw new InvalidOperationException(
			"StandX wallet credentials are required for order entry.");

	private DateTime ServerTime => _restClient?.ServerTime ?? DateTime.UtcNow;

	private void EnsureConnected()
	{
		if (_restClient is null || _marketSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady()
	{
		EnsureConnected();
		if (!RestClient.IsAuthenticated || _orderSocket is null ||
			_portfolioName.IsEmpty())
			throw new InvalidOperationException(
				"StandX wallet private key is required for private operations.");
	}

	private StandXSymbolInfo GetInstrument(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not StandX.");
		var symbol = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _instruments.TryGetValue(symbol, out var instrument)
				? instrument
				: throw new InvalidOperationException(
					$"Unknown StandX market '{symbol}'.");
	}

	private StandXSymbolInfo[] GetInstruments()
	{
		using (_sync.EnterScope())
			return [.. _instruments.Values];
	}

	private bool AddStreamReference(StreamKey key)
	{
		if (_streamReferences.TryGetValue(key, out var count))
		{
			_streamReferences[key] = count + 1;
			return false;
		}
		_streamReferences.Add(key, 1);
		return true;
	}

	private bool ReleaseStreamReference(StreamKey key)
	{
		if (!_streamReferences.TryGetValue(key, out var count))
			return false;
		if (count > 1)
		{
			_streamReferences[key] = count - 1;
			return false;
		}
		_streamReferences.Remove(key);
		return true;
	}

	private long GetTransactionId(string clientOrderId)
	{
		if (clientOrderId.IsEmpty())
			return 0;
		using (_sync.EnterScope())
			if (_transactionByClientOrderId.TryGetValue(clientOrderId,
				out var transactionId))
				return transactionId;
		const string prefix = "SS-";
		return clientOrderId.StartsWith(prefix, StringComparison.Ordinal) &&
			long.TryParse(clientOrderId.AsSpan(prefix.Length), NumberStyles.None,
				CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: 0;
	}

	private static string CreateClientOrderId(long transactionId,
		string userOrderId)
	{
		var value = userOrderId.IsEmpty()
			? "SS-" + transactionId.ToString(CultureInfo.InvariantCulture)
			: userOrderId.Trim();
		if (value.Length is < 1 or > 64 || value.Any(char.IsControl))
			throw new ArgumentException(
				"StandX client order ID must contain 1 to 64 printable characters.",
				nameof(userOrderId));
		return value;
	}

	private bool TryAcceptUserTrade(long tradeId)
	{
		if (tradeId <= 0)
			return false;
		using (_sync.EnterScope())
		{
			var added = _seenUserTradeIds.Add(tradeId);
			if (_seenUserTradeIds.Count > 8192)
			{
				foreach (var old in _seenUserTradeIds.OrderBy(static id => id)
					.Take(_seenUserTradeIds.Count - 4096).ToArray())
					_seenUserTradeIds.Remove(old);
			}
			return added;
		}
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
			_transactionByClientOrderId.Clear();
			_seenUserTradeIds.Clear();
		}
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_portfolioName = null;
		_nextTimeSynchronization = default;
		_nextCandlePoll = default;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_orderSocket?.Dispose();
		_marketSocket?.Dispose();
		_restClient?.Dispose();
		base.DisposeManaged();
	}
}
