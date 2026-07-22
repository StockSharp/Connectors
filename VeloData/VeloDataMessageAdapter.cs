namespace StockSharp.VeloData;

/// <summary>The message adapter for Velo Data market analytics and news.</summary>
[MediaIcon(Media.MediaNames.velodata)]
[Doc("topics/api/connectors/crypto_exchanges/velo_data.html")]
[Display(ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.VeloDataKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
    MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 |
    MessageAdapterCategories.Candles | MessageAdapterCategories.News)]
public partial class VeloDataMessageAdapter : MessageAdapter, ITokenAdapter
{
    private sealed class LiveNewsSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId? SecurityId { get; init; }
        public string Coin { get; init; }
        public long? Remaining { get; set; }
    }

    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _streamGate = new(1, 1);
    private readonly Dictionary<string, VeloDataInstrument> _instruments =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, LiveNewsSubscription> _liveNews = [];
    private VeloDataRestClient _rest;
    private VeloDataNewsSocketClient _newsSocket;

    /// <summary>Initializes a new instance.</summary>
    public VeloDataMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
        this.AddMarketDataSupport();
        this.RemoveTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(VeloDataExtensions.TimeFrames);
    }

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } = [BoardCodes.VeloData];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType) => false;

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.IsAssociated(BoardCodes.VeloData);

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClientsAsync().AsTask().GetAwaiter().GetResult();
        _streamGate.Dispose();
        base.DisposeManaged();
    }
}
