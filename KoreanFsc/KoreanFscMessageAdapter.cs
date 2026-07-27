namespace StockSharp.KoreanFsc;

public partial class KoreanFscMessageAdapter
{
    private static readonly DataType _dailyCandles =
        TimeSpan.FromDays(1).TimeFrame().Immutable();

    private KoreanFscRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public KoreanFscMessageAdapter(
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
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Korean FSC address must be an absolute HTTPS URI.");
        }

        _ = DataSet.ToEndpoint();
        _ = Market.ToApiCode();
        if (LatestSearchDays is < 1 or > 366)
        {
            throw new InvalidOperationException(
                "Korean FSC latest search days must be from 1 to 366.");
        }
        if (PageSize is < 1 or > 10000)
        {
            throw new InvalidOperationException(
                "Korean FSC page size must be from 1 to 10000.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Korean FSC maximum pages must be from 1 to 1000.");
        }

        _client = new KoreanFscRestClient(
            Address,
            Token.UnSecure().Trim())
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

    private KoreanFscRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
