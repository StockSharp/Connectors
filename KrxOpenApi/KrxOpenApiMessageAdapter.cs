namespace StockSharp.KrxOpenApi;

public partial class KrxOpenApiMessageAdapter
{
    private static readonly DataType _dailyCandles =
        TimeSpan.FromDays(1).TimeFrame().Immutable();

    private KrxRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public KrxOpenApiMessageAdapter(
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
        [BoardCodes.Krx];

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
        if (Token.IsEmpty())
        {
            throw new InvalidOperationException(
                LocalizedStrings.TokenNotSpecified);
        }
        if (Address is null || !Address.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                "KRX production API address must be absolute.");
        }
        if (SampleAddress is null || !SampleAddress.IsAbsoluteUri)
        {
            throw new InvalidOperationException(
                "KRX sample API address must be absolute.");
        }
        if (LatestSearchDays is < 1 or > 366)
        {
            throw new InvalidOperationException(
                "KRX latest search days must be from 1 to 366.");
        }
        if (MaxRequests is < 1 or > 10000)
        {
            throw new InvalidOperationException(
                "KRX maximum requests must be from 1 to 10000.");
        }

        _client = new KrxRestClient(
            IsDemo ? SampleAddress : Address,
            Token.UnSecure())
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

    private KrxRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
