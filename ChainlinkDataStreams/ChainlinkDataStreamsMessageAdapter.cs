namespace StockSharp.ChainlinkDataStreams;

/// <summary>The message adapter for Chainlink Data Streams.</summary>
[MediaIcon(Media.MediaNames.chainlinkdatastreams)]
[Doc("topics/api/connectors/crypto_exchanges/chainlink_data_streams.html")]
[Display(
    ResourceType = typeof(LocalizedStrings),
    Name = LocalizedStrings.ChainlinkDataStreamsKey,
    Description = LocalizedStrings.MarketDataConnectorKey,
    GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.Stock |
    MessageAdapterCategories.FX | MessageAdapterCategories.Crypto |
    MessageAdapterCategories.Futures | MessageAdapterCategories.Commodities |
    MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
    MessageAdapterCategories.Paid | MessageAdapterCategories.Level1)]
public partial class ChainlinkDataStreamsMessageAdapter : MessageAdapter,
    IKeySecretAdapter
{
    private sealed class LiveSubscription
    {
        public long TransactionId { get; init; }
        public SecurityId SecurityId { get; init; }
        public ChainlinkFeedInfo Feed { get; init; }
        public ChainlinkStreamPool Pool { get; init; }
        public long? Remaining { get; set; }
        public DateTime? LastObservationTime { get; set; }
        public string LastUpdateKey { get; set; }
    }

    private readonly Lock _sync = new();
    private readonly Dictionary<string, ChainlinkFeedInfo> _feeds =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, LiveSubscription> _liveSubscriptions = [];
    private readonly List<ChainlinkStreamPool> _retiredPools = [];
    private ChainlinkRestClient _rest;
    private string[] _origins = [];

    /// <summary>Initializes a new instance.</summary>
    public ChainlinkDataStreamsMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        ReConnectionSettings.TimeOutInterval = TimeSpan.FromMinutes(2);
        this.AddMarketDataSupport();
        this.RemoveTransactionalSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
    }

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.ChainlinkDataStreams];

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType) => false;

    /// <inheritdoc />
    protected override bool ValidateSecurityId(SecurityId securityId)
        => securityId.IsAssociated(BoardCodes.ChainlinkDataStreams);

    /// <inheritdoc />
    protected override void DisposeManaged()
    {
        DisposeClientsAsync().AsTask().GetAwaiter().GetResult();
        base.DisposeManaged();
    }
}
