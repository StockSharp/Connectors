namespace StockSharp.GMTrade;

using Native;

public partial class GMTradeMessageAdapter
{
	private class MarketSubscription
	{
		public long TransactionId { get; init; }
		public string MarketToken { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public string IndexToken { get; init; }
		public int Resolution { get; init; }
		public TimeSpan TimeFrame { get; init; }
	}

	private sealed class CandleStreamReference
	{
		public int Count { get; set; }
		public string SocketId { get; set; }
	}

	private readonly record struct CandleStreamKey(
		string IndexToken, int Resolution);

	private readonly Lock _sync = new();
	private readonly Dictionary<string, GMTradeMarketInfo> _marketsByCode =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, GMTradeMarketInfo> _marketsByToken =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, GMTradeTokenInfo> _tokensByMint =
		new(StringComparer.Ordinal);
	private readonly Dictionary<long, MarketSubscription>
		_level1Subscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription>
		_candleSubscriptions = [];
	private readonly Dictionary<CandleStreamKey, CandleStreamReference>
		_candleStreams = [];
	private readonly HashSet<string> _seenPublicTrades =
		new(StringComparer.Ordinal);
	private readonly HashSet<string> _seenAccountTrades =
		new(StringComparer.Ordinal);
	private readonly Dictionary<string, decimal> _balanceFingerprints =
		new(StringComparer.Ordinal);
	private GMTradeRestClient _restClient;
	private GMTradeRpcClient _rpcClient;
	private GMTradeGraphQlWebSocketClient _marketSocket;
	private GMTradeGraphQlWebSocketClient _candleSocket;
	private string _marketStreamId;
	private string _positionStreamId;
	private string _orderStreamId;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private string _portfolioName;
	private DateTime _serverTime;
	private DateTime _nextTradePoll;
	private DateTime _nextBalancePoll;
	private DateTime _lastPublicTradePoll;
	private DateTime _lastAccountTradePoll;

	/// <summary>
	/// Initializes a new instance of the <see cref="GMTradeMessageAdapter"/>.
	/// </summary>
	public GMTradeMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddSupportedMessage(MessageTypes.PortfolioLookup, false);
		this.AddSupportedMessage(MessageTypes.OrderStatus, false);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedCandleTimeFrames(GMTradeExtensions.TimeFrames);
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
	public override string[] AssociatedBoards => [BoardCodes.GMTrade];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.GMTrade) ||
			securityId.IsAssociated(BoardCodes.GMTrade);

	private GMTradeRestClient RestClient => _restClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GMTradeRpcClient RpcClient => _rpcClient ?? throw new
		InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GMTradeGraphQlWebSocketClient MarketSocket => _marketSocket ??
		throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private GMTradeGraphQlWebSocketClient CandleSocket => _candleSocket ??
		throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private DateTime ServerTime
	{
		get
		{
			using (_sync.EnterScope())
				return _serverTime == default ? DateTime.UtcNow : _serverTime;
		}
	}

	private void EnsureConnected()
	{
		if (_restClient is null || _marketSocket is null ||
			_candleSocket is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsureWallet()
	{
		EnsureConnected();
		if (_rpcClient?.IsWalletAvailable != true || _portfolioName.IsEmpty())
			throw new InvalidOperationException(
				"A Solana wallet address is required for GMTrade account data.");
	}

	private GMTradeMarketInfo GetMarket(SecurityId securityId)
	{
		if (!ValidateSecurityId(securityId))
			throw new InvalidOperationException(
				$"Security board '{securityId.BoardCode}' is not GMTrade.");
		var code = securityId.SecurityCode.ThrowIfEmpty(
			nameof(securityId.SecurityCode)).Trim();
		using (_sync.EnterScope())
			return _marketsByCode.TryGetValue(code, out var market)
				? market
				: throw new InvalidOperationException(
					$"Unknown GMTrade market '{code}'.");
	}

	private bool TryGetMarketByToken(string marketToken,
		out GMTradeMarketInfo market)
	{
		using (_sync.EnterScope())
			return _marketsByToken.TryGetValue(marketToken ?? string.Empty,
				out market);
	}

	private GMTradeMarketInfo[] GetMarkets()
	{
		using (_sync.EnterScope())
			return [.. _marketsByCode.Values];
	}

	private bool TryGetToken(string mint, out GMTradeTokenInfo token)
	{
		using (_sync.EnterScope())
			return _tokensByMint.TryGetValue(mint ?? string.Empty, out token);
	}

	private static string CreatePortfolioName(string walletAddress)
	{
		walletAddress = walletAddress.NormalizePublicKey(nameof(walletAddress));
		return "GMTRADE_" + walletAddress[..12].ToUpperInvariant();
	}

	private bool TryAcceptTrade(HashSet<string> cache, string id)
	{
		if (id.IsEmpty())
			return false;
		using (_sync.EnterScope())
		{
			var added = cache.Add(id);
			if (cache.Count > 16384)
			{
				foreach (var old in cache.Take(cache.Count - 8192).ToArray())
					cache.Remove(old);
			}
			return added;
		}
	}

	private void ClearState()
	{
		using (_sync.EnterScope())
		{
			_marketsByCode.Clear();
			_marketsByToken.Clear();
			_tokensByMint.Clear();
			_level1Subscriptions.Clear();
			_tickSubscriptions.Clear();
			_candleSubscriptions.Clear();
			_candleStreams.Clear();
			_seenPublicTrades.Clear();
			_seenAccountTrades.Clear();
			_balanceFingerprints.Clear();
			_serverTime = default;
		}
		_marketStreamId = null;
		_positionStreamId = null;
		_orderStreamId = null;
		_portfolioSubscriptionId = 0;
		_orderStatusSubscriptionId = 0;
		_portfolioName = null;
		_nextTradePoll = default;
		_nextBalancePoll = default;
		_lastPublicTradePoll = default;
		_lastAccountTradePoll = default;
	}

	/// <inheritdoc />
	protected override void DisposeManaged()
	{
		_candleSocket?.Dispose();
		_marketSocket?.Dispose();
		_rpcClient?.Dispose();
		_restClient?.Dispose();
		base.DisposeManaged();
	}
}
