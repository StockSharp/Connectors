namespace StockSharp.CowProtocol;

public partial class CowProtocolMessageAdapter
{
    private const int _maximumDeliveryKeys = 100_000;

    private sealed class Level1Subscription
    {
        public CowProtocolMarket Market { get; init; }
    }

    private sealed class TickSubscription
    {
        public CowProtocolMarket Market { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public DateTime LastTime { get; set; }
        public BigInteger LastBlock { get; set; }
        public int Maximum { get; init; }
        public int Delivered { get; set; }
    }

    private sealed class CandleSubscription
    {
        public CowProtocolMarket Market { get; init; }
        public TimeSpan TimeFrame { get; init; }
        public DateTime? To { get; init; }
        public DateTime LastTime { get; set; }
        public int Maximum { get; init; }
        public int Delivered { get; set; }
    }

    private sealed class TrackedOrder
    {
        public long TransactionId { get; init; }
        public string Uid { get; init; }
        public CowProtocolMarket Market { get; init; }
        public Sides Side { get; init; }
        public decimal Volume { get; init; }
        public decimal Price { get; init; }
        public OrderTypes OrderType { get; init; }
        public DateTime SubmittedTime { get; init; }
        public CowProtocolOrder Order { get; set; }
    }

    private sealed class OrderSubscription
    {
        public string Uid { get; init; }
        public SecurityId SecurityId { get; init; }
        public Sides? Side { get; init; }
        public decimal? Volume { get; init; }
        public OrderStates[] States { get; init; }
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public int Skip { get; init; }
        public int Maximum { get; init; }
    }

    private readonly record struct DeliveryKey(long SubscriptionId,
        string Identity);
    private readonly record struct CandleFingerprint(decimal Open,
        decimal High, decimal Low, decimal Close, decimal Volume,
        int TradeCount);
    private readonly record struct BalanceFingerprint(decimal Current,
        decimal Blocked);
    private readonly record struct OrderFingerprint(OrderStates State,
        decimal Balance, decimal Filled);

    private readonly Lock _sync = new();
    private readonly Dictionary<string, CowProtocolMarket> _markets =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CowProtocolMarket> _marketsByPair =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CowProtocolToken> _tokens =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, Level1Subscription>
        _level1Subscriptions = [];
    private readonly Dictionary<long, TickSubscription> _tickSubscriptions =
        [];
    private readonly Dictionary<long, CandleSubscription>
        _candleSubscriptions = [];
    private readonly HashSet<DeliveryKey> _seenTrades = [];
    private readonly Queue<DeliveryKey> _tradeDeliveryOrder = [];
    private readonly Dictionary<string, CandleFingerprint>
        _candleFingerprints = new(StringComparer.Ordinal);
    private readonly HashSet<long> _portfolioSubscriptions = [];
    private readonly Dictionary<long, OrderSubscription> _orderSubscriptions =
        [];
    private readonly Dictionary<string, TrackedOrder> _trackedOrders =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BalanceFingerprint>
        _balanceFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OrderFingerprint>
        _orderFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _sentOrderTrades =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<BigInteger, DateTime> _blockTimes = [];
    private readonly Queue<BigInteger> _blockTimeOrder = [];
    private CowProtocolRpcClient _rpcClient;
    private CowProtocolHttpClient _httpClient;
    private DateTime _nextMarketPoll;
    private DateTime _nextPrivatePoll;

    /// <summary>Initializes the adapter.</summary>
    public CowProtocolMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        HeartbeatInterval = TimeSpan.FromSeconds(1);
        this.AddMarketDataSupport();
        this.AddTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(AllTimeFrames);
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
    public override string[] AssociatedBoards => [BoardCodes.CowProtocol];

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.BoardCode.IsEmpty() ||
            securityId.BoardCode.EqualsIgnoreCase(BoardCodes.CowProtocol) ||
            securityId.IsAssociated(BoardCodes.CowProtocol);

    private CowProtocolRpcClient RpcClient => _rpcClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private CowProtocolHttpClient HttpClient => _httpClient ?? throw new
        InvalidOperationException(LocalizedStrings.ConnectionNotOk);

    private void EnsureConnected()
    {
        if (_rpcClient is null || _httpClient is null)
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
    }

    private void EnsureTradingReady()
    {
        EnsureConnected();
        if (!RpcClient.IsSigningAvailable)
            throw new InvalidOperationException(
                "An EVM private key is required for CoW Protocol orders.");
    }

    private CowProtocolMarket GetMarket(SecurityId securityId)
    {
        if (!ValidateSecurityId(securityId))
            throw new InvalidOperationException(
                $"Security board '{securityId.BoardCode}' is not CoW Protocol.");
        var code = securityId.SecurityCode.ThrowIfEmpty(
            nameof(securityId)).Trim().ToUpperInvariant();
        using (_sync.EnterScope())
            return _markets.TryGetValue(code, out var market)
                ? market
                : throw new InvalidOperationException(
                    $"Unknown CoW Protocol market '{code}'.");
    }

    private CowProtocolMarket GetMarketByTokens(string sellToken,
        string buyToken)
    {
        var key = CreatePairKey(sellToken, buyToken);
        using (_sync.EnterScope())
            return _marketsByPair.TryGetValue(key, out var market)
                ? market
                : null;
    }

    private string GetPortfolioName()
        => $"CoW_{Chain}_{RpcClient.WalletAddress[2..10]}";

    private void ValidatePortfolio(string portfolioName)
    {
        if (!RpcClient.IsWalletConfigured)
            throw new InvalidOperationException(
                "An EVM wallet address is required for portfolio data.");
        if (!portfolioName.IsEmpty() &&
            !portfolioName.EqualsIgnoreCase(GetPortfolioName()))
            throw new InvalidOperationException(
                $"Unknown CoW Protocol portfolio '{portfolioName}'.");
    }

    private async ValueTask<CowProtocolQuote> GetQuoteAsync(
        CowProtocolMarket market, CowProtocolTradeTypes tradeType,
        BigInteger amount, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(market);
        if (!System.Enum.IsDefined(tradeType))
            throw new ArgumentOutOfRangeException(nameof(tradeType), tradeType,
                "Unsupported CoW Protocol trade type.");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        var isExactInput = tradeType == CowProtocolTradeTypes.ExactInput;
        var request = new CowProtocolQuoteRequest
        {
            SellToken = isExactInput
                ? market.BaseToken.Address
                : market.QuoteToken.Address,
            BuyToken = isExactInput
                ? market.QuoteToken.Address
                : market.BaseToken.Address,
            Receiver = CowProtocolExtensions.NativeTokenAddress,
            From = RpcClient.WalletAddress,
            Kind = isExactInput
                ? CowProtocolOrderKinds.Sell
                : CowProtocolOrderKinds.Buy,
            SellAmountAfterFee = isExactInput
                ? amount.ToString(CultureInfo.InvariantCulture)
                : null,
            BuyAmountAfterFee = isExactInput
                ? null
                : amount.ToString(CultureInfo.InvariantCulture),
            ValidFor = checked((uint)OrderValidity.TotalSeconds),
            AppData = CowProtocolExtensions.EmptyAppData,
            SellTokenBalance = CowProtocolTokenBalances.Erc20,
            BuyTokenBalance = CowProtocolTokenBalances.Erc20,
            PriceQuality = CowProtocolPriceQualities.Verified,
            SigningScheme = CowProtocolSigningSchemes.Eip712,
        };
        var response = await HttpClient.GetQuoteAsync(request,
            cancellationToken);
        if (response?.Quote is not { } quote || !response.IsVerified)
            throw new InvalidDataException(
                "CoW Protocol returned an unverified or empty quote.");
        if (!response.From.NormalizeAddress().EqualsIgnoreCase(
            RpcClient.WalletAddress) ||
            !quote.SellToken.NormalizeAddress().EqualsIgnoreCase(
                request.SellToken) ||
            !quote.BuyToken.NormalizeAddress().EqualsIgnoreCase(
                request.BuyToken) || quote.Kind != request.Kind ||
            quote.IsPartiallyFillable ||
            quote.SellTokenBalance != CowProtocolTokenBalances.Erc20 ||
            quote.BuyTokenBalance != CowProtocolTokenBalances.Erc20 ||
            quote.SigningScheme != CowProtocolSigningSchemes.Eip712)
            throw new InvalidDataException(
                "CoW Protocol quote does not match the request.");
        if (!quote.Receiver.NormalizeAddress().EqualsIgnoreCase(
            CowProtocolExtensions.NativeTokenAddress) ||
            !quote.AppDataHash.NormalizeBytes32().EqualsIgnoreCase(
                CowProtocolExtensions.EmptyAppDataHash) ||
            quote.AppData != CowProtocolExtensions.EmptyAppData)
            throw new InvalidDataException(
                "CoW Protocol quote returned unexpected receiver or app data.");

        var sell = quote.SellAmount.ParseInteger();
        var buy = quote.BuyAmount.ParseInteger();
        var fee = quote.FeeAmount.ParseInteger();
        if (sell <= 0 || buy <= 0 || fee < 0 ||
            isExactInput && sell != amount || !isExactInput && buy != amount)
            throw new InvalidDataException(
                "CoW Protocol quote returned invalid token amounts.");
        var validTo = new BigInteger(quote.ValidTo).ToUtcTime();
        var expiration = response.Expiration.ParseApiTime("quote expiration");
        if (validTo <= DateTime.UtcNow || expiration <= DateTime.UtcNow)
            throw new InvalidDataException(
                "CoW Protocol returned an expired quote.");
        return new()
        {
            InputAmount = sell,
            OutputAmount = buy,
            EstimatedFeeAmount = fee,
            QuoteId = response.Id,
            Expiration = expiration,
            Parameters = quote,
        };
    }

    private static string CreatePairKey(string first, string second)
    {
        first = first.NormalizeAddress();
        second = second.NormalizeAddress();
        return string.Compare(first, second, StringComparison.OrdinalIgnoreCase) < 0
            ? first + ":" + second
            : second + ":" + first;
    }

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClients();
        base.DisposeManaged();
    }
}
