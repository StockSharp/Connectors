namespace StockSharp.QuiverQuant;

public partial class QuiverQuantMessageAdapter
{
    private QuiverQuantRestClient _client;
    private string _corporateDonorCycle;

    /// <summary>Initializes a new instance.</summary>
    public QuiverQuantMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.News);
        foreach (var dataType in QuiverQuantDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [QuiverQuantExtensions.DefaultBoard];

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
                "Quiver Quantitative address must be an absolute HTTPS URI.");
        }
        if (PageSize is < 1 or > 500)
        {
            throw new InvalidOperationException(
                "Quiver Quantitative page size must be from 1 to 500.");
        }
        if (MaxPages is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Quiver Quantitative page limit must be from 1 to 100.");
        }
        if (DatasetLimit is < 1 or > 500)
        {
            throw new InvalidOperationException(
                "Quiver Quantitative dataset limit must be from 1 to 500.");
        }
        if (NewsLimit is < 1 or > 500)
        {
            throw new InvalidOperationException(
                "Quiver Quantitative news limit must be from 1 to 500.");
        }

        _corporateDonorCycle =
            QuiverQuantExtensions.NormalizeCycle(
                CorporateDonorCycle);
        _client = new QuiverQuantRestClient(
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

    private QuiverQuantRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string SafeCorporateDonorCycle()
        => _corporateDonorCycle ??
            QuiverQuantExtensions.NormalizeCycle(
                CorporateDonorCycle);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _corporateDonorCycle = null;
    }
}
