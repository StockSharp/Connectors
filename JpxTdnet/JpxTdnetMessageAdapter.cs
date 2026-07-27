namespace StockSharp.JpxTdnet;

public partial class JpxTdnetMessageAdapter
{
    private JpxTdnetRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public JpxTdnetMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.News);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.News;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.Tse];

    /// <summary>
    /// Download a TDnet full-text PDF, summary PDF, or XBRL ZIP.
    /// </summary>
    public Task<byte[]> DownloadDocumentAsync(
        string disclosureNumber,
        JpxTdnetDocumentFormats format,
        CancellationToken cancellationToken = default)
        => SafeClient().DownloadDocument(
            disclosureNumber, format, cancellationToken);

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

        ValidateHttpsAddress(Address, nameof(Address));
        ValidateHttpsAddress(
            ViewerAddress, nameof(ViewerAddress));
        _ = IndexMode.ToApiCode();

        if (DefaultLookupDays is < 1 or > 1827)
        {
            throw new InvalidOperationException(
                "JPX TDnet default lookup days must be from 1 to 1827.");
        }
        if (MaxDays is < 1 or > 1827)
        {
            throw new InvalidOperationException(
                "JPX TDnet maximum days must be from 1 to 1827.");
        }
        if (DefaultLookupDays > MaxDays)
        {
            throw new InvalidOperationException(
                "JPX TDnet default lookup days cannot exceed maximum days.");
        }
        if (SecurityLookupDays is < 1 or > 366)
        {
            throw new InvalidOperationException(
                "JPX TDnet security lookup days must be from 1 to 366.");
        }
        if (RequestInterval < TimeSpan.FromSeconds(1) ||
            RequestInterval > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "JPX TDnet request interval must be from one second to one minute.");
        }
        if (MaxDocumentSizeMb is < 1 or > 1024)
        {
            throw new InvalidOperationException(
                "JPX TDnet maximum document size must be from 1 to 1024 MB.");
        }

        _client = new JpxTdnetRestClient(
            Address,
            Token.UnSecure()?.Trim(),
            MaxDocumentSizeMb,
            RequestInterval)
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

    private JpxTdnetRestClient SafeClient()
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
