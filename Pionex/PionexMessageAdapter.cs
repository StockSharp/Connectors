namespace StockSharp.Pionex;

public partial class PionexMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(PionexSections Section, string Topic, string Symbol), int> _streamReferences = [];
	private readonly Dictionary<string, PionexSections> _symbolSections = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _spotPrivateSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<(PionexSections Section, string Symbol)> _privateSymbols = [];
	private PionexRestClient _restClient;
	private PionexWsClient _marketClient;
	private PionexWsClient _spotUserClient;
	private PionexWsClient _futuresUserClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public PionexSections Section { get; init; }
	}

	private sealed class DepthSubscription : StreamSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class TickSubscription : StreamSubscription
	{
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription : StreamSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime OpenTime { get; set; }
		public decimal OpenPrice { get; set; }
		public decimal HighPrice { get; set; }
		public decimal LowPrice { get; set; }
		public decimal ClosePrice { get; set; }
		public decimal TotalVolume { get; set; }
		public bool IsInitialized { get; set; }
	}

	private readonly record struct CandleEmission(long TransactionId, string Symbol,
		PionexSections Section, TimeSpan TimeFrame, DateTime OpenTime, decimal OpenPrice,
		decimal HighPrice, decimal LowPrice, decimal ClosePrice, decimal TotalVolume,
		CandleStates State);

	/// <summary>
	/// Initializes a new instance of the <see cref="PionexMessageAdapter"/>.
	/// </summary>
	public PionexMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override bool IsSupportOrderBookIncrements => false;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Pionex, BoardCodes.PionexFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Pionex)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.PionexFutures)
			|| securityId.IsAssociated(BoardCodes.Pionex)
			|| securityId.IsAssociated(BoardCodes.PionexFutures);

	private PionexRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _marketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(PionexSections? section = null)
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException("Pionex API key and secret are required for private operations.");
		if (section == PionexSections.Spot && _spotUserClient is null ||
			section == PionexSections.Futures && _futuresUserClient is null)
			throw new InvalidOperationException($"The requested Pionex {section} section is not enabled.");
	}

	private bool IsSectionEnabled(PionexSections section) => Sections.Contains(section);

	private PionexSections ResolveSection(SecurityId securityId)
	{
		if (securityId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must identify the Pionex market section.");
		var section = securityId.BoardCode.ToSection();
		if (!IsSectionEnabled(section))
			throw new InvalidOperationException($"Pionex {section} market section is not enabled.");
		return section;
	}

	private PionexSections ResolveSection(string symbol)
	{
		using (_sync.EnterScope())
		{
			if (_symbolSections.TryGetValue(symbol, out var section))
				return section;
		}
		return symbol?.EndsWith("_PERP", StringComparison.OrdinalIgnoreCase) == true
			? PionexSections.Futures
			: PionexSections.Spot;
	}

	private static string GetPortfolioName(SecureString key)
		=> $"Pionex_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 64)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static int NormalizeDepth(int? depth) => (depth ?? 20).Min(100).Max(1);

	private static bool AddReference<TKey>(IDictionary<TKey, int> references, TKey key)
	{
		if (references.TryGetValue(key, out var count))
		{
			references[key] = count + 1;
			return false;
		}
		references.Add(key, 1);
		return true;
	}

	private static bool ReleaseReference<TKey>(IDictionary<TKey, int> references, TKey key)
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

	private async ValueTask EnsureSpotPrivateSymbolAsync(string symbol,
		CancellationToken cancellationToken)
	{
		if (_spotUserClient is null)
			return;
		bool subscribe;
		using (_sync.EnterScope())
		{
			subscribe = _spotPrivateSymbols.Add(symbol);
			_privateSymbols.Add((PionexSections.Spot, symbol));
		}
		if (!subscribe)
			return;
		await _spotUserClient.SubscribeAsync("ORDER", symbol, null, cancellationToken);
		await _spotUserClient.SubscribeAsync("FILL", symbol, null, cancellationToken);
	}

	private void RememberPrivateSymbol(PionexSections section, string symbol)
	{
		if (symbol.IsEmpty())
			return;
		using (_sync.EnterScope())
			_privateSymbols.Add((section, symbol));
	}
}
