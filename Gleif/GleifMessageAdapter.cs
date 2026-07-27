namespace StockSharp.Gleif;

public partial class GleifMessageAdapter
{
    private GleifRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public GleifMessageAdapter(IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => false;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } = ["GLEIF"];

    /// <inheritdoc />
    protected override async ValueTask ConnectAsync(
        ConnectMessage connectMsg,
        CancellationToken cancellationToken)
    {
        if (_client is not null)
            throw new InvalidOperationException(
                LocalizedStrings.NotDisconnectPrevTime);
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "GLEIF address must be an absolute HTTPS URI.");
        if (PageSize is < 1 or > 200)
            throw new InvalidOperationException(
                "GLEIF page size must be from 1 to 200.");
        if (MaxPages is < 1 or > 1000)
            throw new InvalidOperationException(
                "GLEIF maximum pages must be from 1 to 1000.");

        _client = new GleifRestClient(Address)
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
            throw new InvalidOperationException(
                LocalizedStrings.ConnectionNotOk);
        DisposeClient();
        await base.DisconnectAsync(disconnectMsg, cancellationToken);
    }

    /// <inheritdoc />
    protected override async ValueTask ResetAsync(
        ResetMessage resetMsg,
        CancellationToken cancellationToken)
    {
        DisposeClient();
        await base.ResetAsync(resetMsg, cancellationToken);
    }

    private GleifRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
