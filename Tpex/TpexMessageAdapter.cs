namespace StockSharp.Tpex;

public partial class TpexMessageAdapter
{
    private static readonly DataType _dailyCandles =
        TimeSpan.FromDays(1).TimeFrame().Immutable();

    private TpexRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public TpexMessageAdapter(
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
        [TpexExtensions.BoardCode];

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
                "TPEx address must be an absolute HTTPS URI.");
        }

        _ = Market.IncludesMainboard();
        _ = Market.IncludesEmerging();
        if (CacheTimeout < TimeSpan.Zero ||
            CacheTimeout > TimeSpan.FromDays(1))
        {
            throw new InvalidOperationException(
                "TPEx cache timeout must be from zero to one day.");
        }
        if (MaxHistoryMonths is < 1 or > 1200)
        {
            throw new InvalidOperationException(
                "TPEx maximum history months must be from 1 to 1200.");
        }

        _client = new TpexRestClient(Address)
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

    private TpexRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private Task<TpexSnapshot> LoadSnapshot(
        CancellationToken cancellationToken)
        => SafeClient().GetSnapshot(
            Market,
            IncludeValuations,
            CacheTimeout,
            cancellationToken);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
