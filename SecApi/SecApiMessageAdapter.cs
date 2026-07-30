namespace StockSharp.SecApi;

public partial class SecApiMessageAdapter
{
    private SecApiRestClient _client;
    private string[] _formTypes;

    /// <summary>Initializes a new instance.</summary>
    public SecApiMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.News);

        foreach (var dataType in SecApiDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.News;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [SecApiExtensions.DefaultBoard];

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
                "SEC-API.io address must be an absolute HTTPS URI.");
        }
        if (DefaultExchange.IsEmpty())
        {
            throw new InvalidOperationException(
                "SEC-API.io default exchange is required.");
        }
        if (ResultLimit is < 1 or > 50)
        {
            throw new InvalidOperationException(
                "SEC-API.io result limit must be from 1 to 50.");
        }
        _formTypes = SecApiExtensions.ParseFormTypes(FormTypes);

        _client = new SecApiRestClient(
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

    private SecApiRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string[] SafeFormTypes()
        => _formTypes ??
            SecApiExtensions.ParseFormTypes(FormTypes);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _formTypes = null;
    }
}
