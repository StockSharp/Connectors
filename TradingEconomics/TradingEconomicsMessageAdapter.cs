namespace StockSharp.TradingEconomics;

public partial class TradingEconomicsMessageAdapter
{
    private TradingEconomicsRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public TradingEconomicsMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(
            TradingEconomicsExtensions.TimeFrames);

        foreach (var dataType in TradingEconomicsDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [TradingEconomicsExtensions.DefaultBoard];

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
                "Trading Economics address must be an absolute HTTPS URI.");
        }
        if (DefaultMarket.IsEmpty())
        {
            throw new InvalidOperationException(
                "Trading Economics default market suffix is required.");
        }
        if (DefaultMarket.Contains(':') ||
            DefaultMarket.Contains(','))
        {
            throw new InvalidOperationException(
                "Trading Economics default market suffix is invalid.");
        }
        if (DefaultSearch.IsEmpty())
        {
            throw new InvalidOperationException(
                "Trading Economics default security search is required.");
        }
        if (NewsLimit is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Trading Economics news limit must be from 1 to 1000.");
        }

        _client = new TradingEconomicsRestClient(
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

    private TradingEconomicsRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
