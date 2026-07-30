namespace StockSharp.UnusualWhales;

public partial class UnusualWhalesMessageAdapter
{
    private UnusualWhalesRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public UnusualWhalesMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(
            UnusualWhalesExtensions.TimeFrames);

        foreach (var dataType in UnusualWhalesDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [UnusualWhalesExtensions.DefaultBoard];

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
                "Unusual Whales address must be an absolute HTTPS URI.");
        }
        if (CandleLimit is < 1 or > 2500)
        {
            throw new InvalidOperationException(
                "Unusual Whales candle limit must be from 1 to 2500.");
        }
        if (NewsLimit is < 1 or > 2000)
        {
            throw new InvalidOperationException(
                "Unusual Whales news limit must be from 1 to 2000.");
        }
        if (MaxPages is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Unusual Whales page limit must be from 1 to 100.");
        }
        if (DatasetLimit is < 1 or > 500)
        {
            throw new InvalidOperationException(
                "Unusual Whales dataset limit must be from 1 to 500.");
        }

        _client = new UnusualWhalesRestClient(
            Address, Token.UnSecure())
        {
            Parent = this,
        };
        try
        {
            await base.ConnectAsync(connectMsg, cancellationToken);
        }
        catch
        {
            DisposeClient();
            throw;
        }
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

    private UnusualWhalesRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
