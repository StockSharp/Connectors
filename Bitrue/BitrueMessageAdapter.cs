namespace StockSharp.Bitrue;

public partial class BitrueMessageAdapter
{
	private readonly Lock _sync = new();
	private readonly Dictionary<long, MarketSubscription> _level1Subscriptions = [];
	private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
	private readonly Dictionary<long, MarketSubscription> _tickSubscriptions = [];
	private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
	private readonly Dictionary<StreamKey, int> _streamReferences = [];
	private readonly Dictionary<string, BitrueSpotSymbol> _spotSymbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, BitrueFuturesContract> _futuresContracts =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _futuresPrivateSymbols =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _spotOrderSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _futuresOrderSymbols = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, long> _spotLastTradeIds =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _seenFillIds = new(StringComparer.OrdinalIgnoreCase);
	private BitrueSpotRestClient _spotRestClient;
	private BitrueFuturesRestClient _futuresRestClient;
	private BitruePublicWsClient _spotPublicWsClient;
	private BitruePublicWsClient _futuresPublicWsClient;
	private BitruePrivateWsClient _spotPrivateWsClient;
	private BitruePrivateWsClient _futuresPrivateWsClient;
	private string _spotListenKey;
	private string _futuresListenKey;
	private DateTime _nextSpotListenKeyExtension;
	private DateTime _nextFuturesListenKeyExtension;
	private DateTime _lastPollingTime;
	private long _portfolioSubscriptionId;
	private long _orderStatusSubscriptionId;
	private long _publicTradeId;
	private bool _instrumentsLoaded;

	private class MarketSubscription
	{
		public string Symbol { get; init; }
		public BitrueSections Section { get; init; }
	}

	private sealed class DepthSubscription : MarketSubscription
	{
		public int Depth { get; init; }
	}

	private sealed class CandleSubscription : MarketSubscription
	{
		public TimeSpan TimeFrame { get; init; }
	}

	private readonly record struct StreamKey(BitrueSections Section, BitrueWsTopics Topic,
		string Symbol, TimeSpan TimeFrame);

	/// <summary>
	/// Initializes a new instance of the <see cref="BitrueMessageAdapter"/>.
	/// </summary>
	public BitrueMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		HeartbeatInterval = TimeSpan.FromSeconds(1);
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.RemoveSupportedMessage(MessageTypes.OrderReplace);
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
	public override string[] AssociatedBoards => [BoardCodes.Bitrue, BoardCodes.BitrueFutures];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty()
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Bitrue)
			|| securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitrueFutures)
			|| securityId.IsAssociated(BoardCodes.Bitrue)
			|| securityId.IsAssociated(BoardCodes.BitrueFutures);

	private bool IsSectionEnabled(BitrueSections section) => Sections.Contains(section);

	private BitrueSpotRestClient SpotRestClient
		=> _spotRestClient ?? throw new InvalidOperationException(
			"The Bitrue spot section is not connected.");

	private BitrueFuturesRestClient FuturesRestClient
		=> _futuresRestClient ?? throw new InvalidOperationException(
			"The Bitrue futures section is not connected.");

	private void EnsureConnected()
	{
		if (_spotRestClient is null && _futuresRestClient is null)
			throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
	}

	private void EnsurePrivateReady(BitrueSections section)
	{
		EnsureConnected();
		var ready = section == BitrueSections.Spot
			? _spotRestClient?.IsCredentialsAvailable == true && _spotPrivateWsClient is not null
			: _futuresRestClient?.IsCredentialsAvailable == true && _futuresPrivateWsClient is not null;
		if (!ready)
			throw new InvalidOperationException(
				$"Bitrue API key and secret are required for private {section} operations.");
	}

	private BitrueSections ResolveSection(SecurityId securityId)
	{
		if (!securityId.BoardCode.IsEmpty())
		{
			var section = securityId.BoardCode.EqualsIgnoreCase(BoardCodes.BitrueFutures)
				? BitrueSections.Futures
				: BitrueSections.Spot;
			if (!IsSectionEnabled(section))
				throw new InvalidOperationException($"Bitrue {section} section is not enabled.");
			return section;
		}

		var enabled = Sections.Distinct().ToArray();
		if (enabled.Length == 1)
			return enabled[0];

		var symbol = securityId.SecurityCode;
		using (_sync.EnterScope())
		{
			if (_futuresContracts.ContainsKey(symbol ?? string.Empty) &&
				!_spotSymbols.ContainsKey(symbol ?? string.Empty))
				return BitrueSections.Futures;
			if (_spotSymbols.ContainsKey(symbol ?? string.Empty) &&
				!_futuresContracts.ContainsKey(symbol ?? string.Empty))
				return BitrueSections.Spot;
		}

		throw new InvalidOperationException(
			"SecurityId.BoardCode must identify the Bitrue spot or futures section.");
	}

	private static string GetSymbol(SecurityId securityId, BitrueSections section)
	{
		var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode))
			.Replace('_', '-')
			.ToUpperInvariant();
		if (section == BitrueSections.Futures && !symbol.StartsWith("E-",
			StringComparison.OrdinalIgnoreCase))
		{
			var compact = symbol.Replace("-", string.Empty, StringComparison.Ordinal);
			if (compact.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
				symbol = $"E-{compact[..^4]}-USDT";
		}
		return section == BitrueSections.Spot
			? symbol.Replace("-", string.Empty, StringComparison.Ordinal)
			: symbol;
	}

	private static string GetPortfolioName(BitrueSections section, SecureString key)
		=> $"Bitrue_{section}_{(key.IsEmpty() ? "Public" : key.ToId())}";

	private string GetPortfolioName(BitrueSections section)
		=> GetPortfolioName(section, Key);

	private static string CreateClientOrderId(long transactionId, string userOrderId)
	{
		if (!userOrderId.IsEmpty() && userOrderId.Length <= 32 &&
			userOrderId.All(static c => char.IsLetterOrDigit(c) || c is '-' or '_'))
			return userOrderId;
		return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
	}

	private static long ParseTransactionId(string clientOrderId)
		=> clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
			long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None,
				CultureInfo.InvariantCulture, out var transactionId)
				? transactionId
				: 0;

	private static int NormalizeDepth(int? depth, BitrueSections section)
	{
		var maximum = section == BitrueSections.Spot ? 1000 : 100;
		var requested = (depth ?? (section == BitrueSections.Spot ? 100 : 30)).Max(1)
			.Min(maximum);
		if (section == BitrueSections.Futures)
			return requested;
		return requested <= 5 ? 5 : requested <= 10 ? 10 : requested <= 20 ? 20
			: requested <= 50 ? 50 : requested <= 100 ? 100 : requested <= 500 ? 500 : 1000;
	}

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
