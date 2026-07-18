namespace StockSharp.Phemex;

public partial class PhemexMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(PhemexSections Section, string Topic, string Symbol), int> _streamReferences = [];
	private readonly Dictionary<PhemexSymbolKey, PhemexSections> _symbolSections = [];
	private readonly HashSet<string> _spotPrivateSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<(PhemexSections Section, string Symbol)> _privateSymbols = [];
	private PhemexRestClient _restClient;
	private PhemexWsClient _marketClient;
	private PhemexWsClient _spotUserClient;
	private PhemexWsClient _futuresUserClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public PhemexSections Section { get; init; }
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
		PhemexSections Section, TimeSpan TimeFrame, DateTime OpenTime, decimal OpenPrice,
		decimal HighPrice, decimal LowPrice, decimal ClosePrice, decimal TotalVolume,
		CandleStates State);

	/// <summary>
	/// Initializes a new instance of the <see cref="PhemexMessageAdapter"/>.
	/// </summary>
	public PhemexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(10);
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
	public override string[] AssociatedBoards => [BoardCodes.Phemex, BoardCodes.PhemexFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Phemex)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.PhemexFutures)
			|| securityId.IsAssociated(BoardCodes.Phemex)
			|| securityId.IsAssociated(BoardCodes.PhemexFutures);

	private PhemexRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null || _marketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(PhemexSections? section = null)
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException("Phemex API key and secret are required for private operations.");
		if (section == PhemexSections.Spot && _spotUserClient is null ||
			section == PhemexSections.Futures && _futuresUserClient is null)
			throw new InvalidOperationException($"The requested Phemex {section} section is not enabled.");
	}

	private bool IsSectionEnabled(PhemexSections section) => Sections.Contains(section);

	private PhemexSections ResolveSection(SecurityId securityId)
	{
		if (securityId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must identify the Phemex market section.");
		var section = securityId.BoardCode.ToSection();
		if (!IsSectionEnabled(section))
			throw new InvalidOperationException($"Phemex {section} market section is not enabled.");
		return section;
	}

	private PhemexSections ResolveSection(string symbol)
	{
		using (_sync.EnterScope())
		{
			if (_symbolSections.TryGetValue(new(symbol?.ToUpperInvariant()), out var section))
				return section;
		}
		return symbol?.StartsWith('s') == true ? PhemexSections.Spot : PhemexSections.Futures;
	}

	private static string GetPortfolioName(SecureString key)
		=> $"Phemex_{(key.IsEmpty() ? "Public" : key.ToId())}";

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

	private static int NormalizeDepth(int? depth) => (depth ?? 20).Min(30).Max(1);

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
			_privateSymbols.Add((PhemexSections.Spot, symbol));
		}
		if (!subscribe)
			return;
		await _spotUserClient.SubscribeAsync("ORDER", symbol, null, cancellationToken);
		await _spotUserClient.SubscribeAsync("FILL", symbol, null, cancellationToken);
	}

	private void RememberPrivateSymbol(PhemexSections section, string symbol)
	{
		if (symbol.IsEmpty())
			return;
		using (_sync.EnterScope())
			_privateSymbols.Add((section, symbol));
	}
}
