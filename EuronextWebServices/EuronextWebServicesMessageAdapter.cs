namespace StockSharp.EuronextWebServices;

public partial class EuronextWebServicesMessageAdapter
{
    private EuronextRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public EuronextWebServicesMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
        this.AddSupportedMarketDataType(DataType.Ticks);
        this.AddSupportedCandleTimeFrames(
        [
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15),
            TimeSpan.FromMinutes(30),
            TimeSpan.FromHours(1),
        ]);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Level1 ||
            dataType == DataType.MarketDepth ||
            dataType == DataType.Ticks ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        ["XAMS", "XBRU", "XDUB", "XLIS", "XMIL", "XOSL", "XPAR"];

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
        var token = Token.UnSecure()?.Trim();
        if (token.IsEmpty() || token.Length > 512)
        {
            throw new InvalidOperationException(
                "Euronext authentication key must contain from 1 to 512 characters.");
        }
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Euronext address must be an absolute HTTPS URI.");
        }
        _ = SessionQuality.ToApiCode();
        if (IntradayDepth is < 1 or > 2)
        {
            throw new InvalidOperationException(
                "Euronext intraday depth must be one or two sessions.");
        }

        _client = new EuronextRestClient(Address, token)
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

    private EuronextRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
