namespace StockSharp.FXOpen;

public partial class FXOpenMessageAdapter
{
    private sealed class FeedSubscription
    {
        public string Symbol { get; init; }
        public DataType DataType { get; init; }
        public int Depth { get; init; }
    }

    private sealed class CandleSubscription
    {
        public string Symbol { get; init; }
        public TimeSpan TimeFrame { get; init; }
        public TickTraderPriceTypes PriceType { get; init; }
    }

    private readonly Lock _sync = new();
    private readonly Dictionary<string, TickTraderSymbol> _symbols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, FeedSubscription> _feedSubscriptions = [];
    private readonly Dictionary<long, CandleSubscription> _candleSubscriptions = [];
    private readonly Dictionary<long, long> _orderTransactions = [];
    private readonly Dictionary<string, long> _clientTransactions =
        new(StringComparer.OrdinalIgnoreCase);
    private FXOpenRestClient _restClient;
    private FXOpenWebSocketClient _webSocketClient;
    private long _portfolioSubscriptionId;
    private long _orderSubscriptionId;
    private string _portfolioName;

    /// <summary>Initializes a new instance of the <see cref="FXOpenMessageAdapter"/> class.</summary>
    public FXOpenMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(15);
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
        this.RemoveSupportedMessage(MessageTypes.OrderGroupCancel);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities || dataType.IsTFCandles ||
            dataType == DataType.Level1 || dataType == DataType.MarketDepth ||
            dataType == DataType.Transactions || dataType == DataType.PositionChanges ||
            base.IsAllDownloadingSupported(dataType);

    /// <inheritdoc />
    public override bool IsSupportCandlesUpdates(MarketDataMessage subscription) => true;

    /// <inheritdoc />
    public override bool IsSupportOrderBookIncrements => false;

    /// <inheritdoc />
    public override bool IsReplaceCommandEditCurrent => true;

    /// <inheritdoc />
    public override bool IsSupportTransactionLog => true;

    /// <inheritdoc />
    public override bool IsSupportExecutionsPnL => true;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } = [BoardCodes.FXOpen];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() || securityId.BoardCode.EqualsIgnoreCase(BoardCodes.FXOpen) ||
            securityId.IsAssociated(BoardCodes.FXOpen);

    private FXOpenRestClient RestClient
        => _restClient ?? throw new InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private FXOpenWebSocketClient WebSocketClient
        => _webSocketClient ?? throw new InvalidOperationException(
            "FXOpen Web API credentials are required for streaming subscriptions.");

    private void EnsurePrivate()
    {
        if (!RestClient.IsCredentialsAvailable)
            throw new InvalidOperationException(
                "FXOpen Web API ID, key, and secret are required for private operations.");
    }

    private TickTraderSymbol ResolveSymbol(SecurityId securityId)
    {
        var symbol = securityId.SecurityCode.ThrowIfEmpty(nameof(securityId.SecurityCode));
        return _symbols.TryGetValue(symbol, out var value)
            ? value
            : throw new InvalidOperationException($"FXOpen symbol '{symbol}' was not found.");
    }

    private static string CreateClientId(long transactionId, string userOrderId)
        => !userOrderId.IsEmpty() ? userOrderId :
            $"ss-{transactionId.ToString(CultureInfo.InvariantCulture)}";

    private static long ParseTransactionId(string clientId)
        => clientId?.StartsWith("ss-", StringComparison.OrdinalIgnoreCase) == true &&
            long.TryParse(clientId.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                ? id : 0;

    private long ResolveTransactionId(string clientId)
    {
        var transactionId = ParseTransactionId(clientId);
        if (transactionId != 0 || clientId.IsEmpty())
            return transactionId;
        using (_sync.EnterScope())
            return _clientTransactions.TryGetValue(clientId, out transactionId)
                ? transactionId : 0;
    }

    private void ValidatePortfolio(string portfolioName)
    {
        if (!portfolioName.IsEmpty() && !portfolioName.EqualsIgnoreCase(_portfolioName))
            throw new InvalidOperationException($"Unknown FXOpen portfolio '{portfolioName}'.");
    }
}
