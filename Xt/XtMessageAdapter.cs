namespace StockSharp.Xt;

public partial class XtMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(XtSections Section, string Topic, string Symbol), int> _streamReferences = [];
	private readonly HashSet<(XtSections Section, string Symbol)> _knownSymbols = [];
	private readonly HashSet<(XtSections Section, string Symbol)> _privateSymbols = [];
	private XtRestClient _restClient;
	private XtWsClient _spotMarketClient;
	private XtWsClient _futuresMarketClient;
	private XtWsClient _spotUserClient;
	private XtWsClient _futuresUserClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public XtSections Section { get; init; }
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
		XtSections Section, TimeSpan TimeFrame, DateTime OpenTime, decimal OpenPrice,
		decimal HighPrice, decimal LowPrice, decimal ClosePrice, decimal TotalVolume,
		CandleStates State);

	/// <summary>
	/// Initializes a new instance of the <see cref="XtMessageAdapter"/>.
	/// </summary>
	public XtMessageAdapter(IdGenerator transactionIdGenerator)
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
	public override string[] AssociatedBoards => [BoardCodes.Xt, BoardCodes.XtFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Xt)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.XtFutures)
			|| securityId.IsAssociated(BoardCodes.Xt)
			|| securityId.IsAssociated(BoardCodes.XtFutures);

	private XtRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null ||
			IsSectionEnabled(XtSections.Spot) && _spotMarketClient is null ||
			IsSectionEnabled(XtSections.Futures) && _futuresMarketClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(XtSections? section = null)
	{
		EnsureConnected();
		if (!RestClient.IsCredentialsAvailable)
			throw new InvalidOperationException("XT.COM API key and secret are required for private operations.");
		if (section == XtSections.Spot && _spotUserClient is null ||
			section == XtSections.Futures && _futuresUserClient is null)
			throw new InvalidOperationException($"The requested XT.COM {section} section is not enabled.");
	}

	private bool IsSectionEnabled(XtSections section) => Sections.Contains(section);

	private XtSections ResolveSection(SecurityId securityId)
	{
		if (securityId.BoardCode.IsEmpty())
			throw new InvalidOperationException("SecurityId.BoardCode must identify the XT.COM market section.");
		var section = securityId.BoardCode.ToSection();
		if (!IsSectionEnabled(section))
			throw new InvalidOperationException($"XT.COM {section} market section is not enabled.");
		return section;
	}

	private XtSections ResolveSection(string symbol)
	{
		XtSections[] sections;
		using (_sync.EnterScope())
			sections = [.. _knownSymbols
				.Where(pair => pair.Symbol.EqualsIgnoreCase(symbol))
				.Select(static pair => pair.Section)
				.Distinct()];
		return sections.Length switch
		{
			1 => sections[0],
			0 => throw new InvalidOperationException(
				$"XT.COM symbol {symbol} is unknown. Run security lookup first."),
			_ => throw new InvalidOperationException(
				$"XT.COM symbol {symbol} exists on multiple sections; specify SecurityId.BoardCode."),
		};
	}

	private static string GetPortfolioName(SecureString key)
		=> $"XT_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length is >= 4 and <= 22 &&
			userOrderId.All(static ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
			return userOrderId;
		return $"ss_{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss_", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static int NormalizeDepth(int? depth)
		=> depth switch
		{
			null or <= 5 => 5,
			<= 10 => 10,
			<= 20 => 20,
			_ => 50,
		};

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

	private ValueTask EnsureSpotPrivateSymbolAsync(string symbol,
		CancellationToken cancellationToken)
	{
		_ = cancellationToken;
		using (_sync.EnterScope())
			_privateSymbols.Add((XtSections.Spot, symbol));
		return default;
	}

	private void RememberPrivateSymbol(XtSections section, string symbol)
	{
		if (symbol.IsEmpty())
			return;
		using (_sync.EnterScope())
			_privateSymbols.Add((section, symbol));
	}
}
