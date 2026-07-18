namespace StockSharp.Weex;

public partial class WeexMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<(WeexSections Section, string Symbol), int> _tickerReferences = [];
	private readonly Dictionary<(WeexSections Section, string Symbol, int Depth), int> _depthReferences = [];
	private readonly Dictionary<(WeexSections Section, string Symbol), int> _tradeReferences = [];
	private readonly Dictionary<(WeexSections Section, string Symbol, TimeSpan TimeFrame), int> _candleReferences = [];
	private readonly Dictionary<string, WeexSections> _orderSections = new(StringComparer.OrdinalIgnoreCase);
	private WeexRestClient _restClient;
	private WeexWsClient _spotMarketClient;
	private WeexWsClient _futuresMarketClient;
	private WeexWsClient _spotUserClient;
	private WeexWsClient _futuresUserClient;
	private string _portfolioName;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;

	private class StreamSubscription
	{
		public string Symbol { get; init; }
		public WeexSections Section { get; init; }
	}

	private sealed class DepthSubscription : StreamSubscription
	{
		public int Depth { get; init; }
		public long LastSequence { get; set; }
	}

	private sealed class TickSubscription : StreamSubscription
	{
		public string LastTradeId { get; set; }
		public DateTime LastTime { get; set; }
	}

	private sealed class CandleSubscription : StreamSubscription
	{
		public TimeSpan TimeFrame { get; init; }
		public DateTime LastOpenTime { get; set; }
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="WeexMessageAdapter"/>.
	/// </summary>
	public WeexMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(20);

		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Ticks);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedCandleTimeFrames(AllTimeFrames);
	}

	/// <inheritdoc />
	public override bool IsAllDownloadingSupported(DataType dataType)
		=> dataType == DataType.Securities || dataType == DataType.Transactions ||
			dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

	/// <inheritdoc />
	public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

	/// <inheritdoc />
	public override bool IsSupportOrderBookIncrements => true;

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Weex, BoardCodes.WeexFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Weex)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WeexFutures)
			|| securityId.IsAssociated(BoardCodes.Weex)
			|| securityId.IsAssociated(BoardCodes.WeexFutures);

	private WeexRestClient RestClient
		=> _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

	private void EnsureConnected()
	{
		if (_restClient is null ||
			(IsSectionEnabled(WeexSections.Spot) && _spotMarketClient is null) ||
			(IsSectionEnabled(WeexSections.Futures) && _futuresMarketClient is null))
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(WeexSections? section = null)
	{
		EnsureConnected();
		if (!RestClient.HasCredentials)
			throw new InvalidOperationException("WEEX API key, secret and passphrase are required for private operations.");
		if (section is WeexSections.Spot && _spotUserClient is null ||
			section is WeexSections.Futures && _futuresUserClient is null)
			throw new InvalidOperationException("The requested WEEX market section is not enabled.");
	}

	private bool IsSectionEnabled(WeexSections section) => Sections.Contains(section);

	private WeexWsClient GetMarketClient(WeexSections section)
		=> section == WeexSections.Spot
			? _spotMarketClient ?? throw new InvalidOperationException("WEEX spot market section is not connected.")
			: _futuresMarketClient ?? throw new InvalidOperationException("WEEX futures market section is not connected.");

	private WeexSections ResolveSection(SecurityId securityId)
	{
		var section = securityId.BoardCode.ToSection();
		if (!IsSectionEnabled(section))
			throw new InvalidOperationException($"WEEX {section} market section is not enabled.");
		return section;
	}

	private static string GetPortfolioName(SecureString key)
		=> $"WEEX_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 36)
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true
			&& long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var id)
				? id
				: 0;

	private static int NormalizeDepth(int? depth) => (depth ?? 15) > 15 ? 200 : 15;

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
}
