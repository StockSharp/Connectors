namespace StockSharp.StockDataOrg;

public partial class StockDataOrgMessageAdapter
{
    private StockDataOrgRestClient _client;
    private TimeZoneInfo _quoteTimeZone;

    /// <summary>Initializes a new instance.</summary>
    public StockDataOrgMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(
            StockDataOrgExtensions.TimeFrames);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.News ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [StockDataOrgExtensions.DefaultBoard];

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
                "StockData.org address must be an absolute HTTPS URI.");
        }
        if (NewsPageSize is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "StockData.org news page size must be from 1 to 100.");
        }
        if (MaxRequests is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "StockData.org maximum requests must be from 1 to 1000.");
        }

        _quoteTimeZone =
            StockDataOrgExtensions.ResolveTimeZone(QuoteTimeZoneId);
        _client = new StockDataOrgRestClient(
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

    private StockDataOrgRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private TimeZoneInfo SafeQuoteTimeZone()
        => _quoteTimeZone ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _quoteTimeZone = null;
    }
}
