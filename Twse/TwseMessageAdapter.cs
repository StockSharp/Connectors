namespace StockSharp.Twse;

public partial class TwseMessageAdapter
{
    private static readonly DataType _dailyCandles =
        TimeSpan.FromDays(1).TimeFrame().Immutable();

    private TwseRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public TwseMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames([TimeSpan.FromDays(1)]);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Level1 ||
            dataType == _dailyCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.Tsec];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_client is not null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        }
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "TWSE OpenAPI address must be an absolute HTTPS URI.");
        }
        if (CacheTimeout < TimeSpan.Zero ||
            CacheTimeout > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                "TWSE cache timeout must be from zero to one day.");
        }

        _client = new TwseRestClient(Address)
        {
            Parent = this,
        };

        await base.ConnectAsync(connectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask DisconnectAsync(
        DisconnectMessage disconnectMsg,
        CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        }

        DisposeClient();
        await base.DisconnectAsync(
            disconnectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private TwseRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private Task<TwseSnapshot> LoadSnapshot(
        CancellationToken cancellationToken)
        => SafeClient().GetSnapshot(
            CacheTimeout,
            IncludeProfiles,
            IncludeValuations,
            cancellationToken);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
