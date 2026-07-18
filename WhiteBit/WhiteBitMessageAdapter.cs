namespace StockSharp.WhiteBit;

public partial class WhiteBitMessageAdapter
{
    private readonly Lock _sync = new();
    private readonly Dictionary<long, StreamSubscription> _level1Subscriptions = [];
    private readonly Dictionary<long, DepthSubscription> _depthSubscriptions = [];
    private readonly Dictionary<long, TickSubscription> _tickSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
    private readonly Dictionary<(string Symbol, TimeSpan TimeFrame), WhiteBitWsClient> _candleWsClients = [];
    private readonly Dictionary<string, int> _marketReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _depthReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _tradeReferences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Symbol, TimeSpan TimeFrame), int> _candleReferences = [];
    private readonly Dictionary<string, string> _marketBoards = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, string> _orderBoards = [];
    private WhiteBitRestClient _restClient;
    private WhiteBitWsClient _marketWsClient;
    private WhiteBitWsClient _userWsClient;
    private string _portfolioName;
    private long _portfolioSubscriptionId;
    private long _orderStatusSubscriptionId;

    private class StreamSubscription
    {
        public string Symbol { get; init; }
        public string BoardCode { get; init; }
    }

    private sealed class DepthSubscription : StreamSubscription
    {
        public int Depth { get; init; }
        public long LastSequence { get; set; }
    }

    private sealed class TickSubscription : StreamSubscription
    {
        public long? LastTradeId { get; set; }
        public DateTime LastTime { get; set; }
    }

    private sealed class CandleSubscription : StreamSubscription
    {
        public TimeSpan TimeFrame { get; init; }
        public DateTime LastOpenTime { get; set; }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WhiteBitMessageAdapter"/>.
    /// </summary>
    public WhiteBitMessageAdapter(IdGenerator transactionIdGenerator)
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
        => dataType == DataType.Securities || dataType == DataType.Transactions ||
            dataType == DataType.PositionChanges || base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards =>
        [BoardCodes.WhiteBit, BoardCodes.WhiteBitMargin, BoardCodes.WhiteBitFutures];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty()
            || securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WhiteBit)
            || securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WhiteBitMargin)
            || securityId.BoardCode.EqualsIgnoreCase(BoardCodes.WhiteBitFutures)
            || securityId.IsAssociated(BoardCodes.WhiteBit)
            || securityId.IsAssociated(BoardCodes.WhiteBitMargin)
            || securityId.IsAssociated(BoardCodes.WhiteBitFutures);

    private WhiteBitRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private WhiteBitWsClient MarketWsClient
        => _marketWsClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_restClient is null || _marketWsClient is null)
            throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);
    }

    private void EnsurePrivateReady()
    {
        EnsureConnected();
        if (Key.IsEmpty() || Secret.IsEmpty() || _userWsClient is null)
            throw new InvalidOperationException("WhiteBIT API key and secret are required for private operations.");
    }

    private bool IsSectionEnabled(WhiteBitSections section) => Sections.Contains(section);

    private static bool IsCollateralBoard(string boardCode)
        => boardCode.EqualsIgnoreCase(BoardCodes.WhiteBitMargin)
            || boardCode.EqualsIgnoreCase(BoardCodes.WhiteBitFutures);

    private static string GetPortfolioName(SecureString key)
        => $"WhiteBIT_{(key.IsEmpty() ? "Public" : key.ToId())}";

    private static SecurityId ToSecurityId(string symbol, string boardCode)
        => symbol.ToStockSharp(boardCode);

    private static int NormalizeDepth(int? depth)
    {
        var value = (depth ?? 100).Max(1);
        return value switch
        {
            <= 1 => 1,
            <= 5 => 5,
            <= 10 => 10,
            <= 20 => 20,
            <= 30 => 30,
            <= 50 => 50,
            _ => 100,
        };
    }

    private static string CreateClientOrderId(long transactionId, string userOrderId)
    {
        if (!userOrderId.IsEmpty() && userOrderId.Length <= 32)
            return userOrderId;
        return $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";
    }

    private static long ParseTransactionId(string clientOrderId)
        => clientOrderId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true
            && long.TryParse(clientOrderId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id
                : 0;

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
