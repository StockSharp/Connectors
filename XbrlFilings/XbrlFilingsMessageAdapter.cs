namespace StockSharp.XbrlFilings;

public partial class XbrlFilingsMessageAdapter
{
    private XbrlFilingsRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public XbrlFilingsMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.News);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.News;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } = [];

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
        ValidateHttpsAddress(Address, nameof(Address));
        ValidateHttpsAddress(PublicAddress, nameof(PublicAddress));
        if (PageSize is < 1 or > 200)
        {
            throw new InvalidOperationException(
                "filings.xbrl.org page size must be from 1 to 200.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "filings.xbrl.org maximum pages must be from 1 to 1000.");
        }

        var country = Country?.Trim().ToUpperInvariant();
        if (!country.IsEmpty() &&
            (country.Length != 2 ||
                country.Any(character =>
                    !char.IsAsciiLetterUpper(character))))
        {
            throw new InvalidOperationException(
                "filings.xbrl.org country must be an ISO 3166-1 alpha-2 code.");
        }

        _client = new XbrlFilingsRestClient(Address)
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

    private XbrlFilingsRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    private static void ValidateHttpsAddress(
        Uri address,
        string parameterName)
    {
        if (address is null ||
            !address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{parameterName} must be an absolute HTTPS URI.");
        }
    }
}
