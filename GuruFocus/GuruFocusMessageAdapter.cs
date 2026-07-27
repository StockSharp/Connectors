namespace StockSharp.GuruFocus;

public partial class GuruFocusMessageAdapter
{
    private GuruFocusRestClient _client;
    private string _regionCode;
    private string _guruTradeActions;

    /// <summary>Initializes a new instance.</summary>
    public GuruFocusMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(
            GuruFocusExtensions.TimeFrames);
        foreach (var dataType in GuruFocusDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [GuruFocusExtensions.DefaultBoard];

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
                "GuruFocus address must be an absolute HTTPS URI.");
        }
        if (PageSize is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "GuruFocus page size must be from 1 to 100.");
        }
        if (MaxLookupPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "GuruFocus lookup page limit must be from 1 to 1000.");
        }
        if (DatasetLimit is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "GuruFocus dataset limit must be from 1 to 100.");
        }
        if (NewsLimit is < 1 or > 200)
        {
            throw new InvalidOperationException(
                "GuruFocus news limit must be from 1 to 200.");
        }
        if (!FilingFormType.IsEmpty() &&
            FilingFormType.Trim().Length > 32)
        {
            throw new InvalidOperationException(
                "GuruFocus SEC form type is too long.");
        }

        _regionCode =
            GuruFocusExtensions.NormalizeRegionCode(RegionCode);
        _guruTradeActions =
            GuruFocusExtensions.NormalizeGuruActions(
                GuruTradeActions);
        _client = new GuruFocusRestClient(
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

    private GuruFocusRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string SafeRegionCode()
        => _regionCode ??
            GuruFocusExtensions.NormalizeRegionCode(RegionCode);

    private string SafeGuruTradeActions()
        => _guruTradeActions ??
            GuruFocusExtensions.NormalizeGuruActions(
                GuruTradeActions);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _regionCode = null;
        _guruTradeActions = null;
    }
}
