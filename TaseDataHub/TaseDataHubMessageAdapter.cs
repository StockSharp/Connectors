namespace StockSharp.TaseDataHub;

public partial class TaseDataHubMessageAdapter
{
    private static readonly DataType _dailyCandles =
        TimeSpan.FromDays(1).TimeFrame().Immutable();

    private readonly SemaphoreSlim _referenceSync = new(1, 1);

    private TaseDataHubRestClient _client;
    private TaseSecurity[] _securities;
    private IReadOnlyDictionary<string, TaseSecurityType> _securityTypes;
    private DateTimeOffset _referenceLoaded;

    /// <summary>Initializes a new instance.</summary>
    public TaseDataHubMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames([TimeSpan.FromDays(1)]);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Level1 ||
            dataType == _dailyCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.Tase];

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
        if (Key.IsEmpty())
        {
            throw new InvalidOperationException(
                "TASE Data Hub OAuth client ID is not specified.");
        }
        if (Secret.IsEmpty())
        {
            throw new InvalidOperationException(
                "TASE Data Hub OAuth client secret is not specified.");
        }
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "TASE Data Hub address must be an absolute HTTPS URI.");
        }
        if (Scope.IsEmpty() || Scope.Length > 200)
        {
            throw new InvalidOperationException(
                "TASE Data Hub OAuth scope must contain from 1 to 200 characters.");
        }
        if (SecurityLookupDays is < 1 or > 31)
        {
            throw new InvalidOperationException(
                "TASE security lookup days must be from 1 to 31.");
        }
        if (ReferenceCacheTimeout < TimeSpan.Zero ||
            ReferenceCacheTimeout > TimeSpan.FromDays(7))
        {
            throw new InvalidOperationException(
                "TASE reference cache timeout must be from zero to seven days.");
        }

        _client = new TaseDataHubRestClient(
            Address,
            Key.UnSecure()?.Trim(),
            Secret.UnSecure()?.Trim(),
            Scope.Trim())
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

    private TaseDataHubRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private async Task<(
        TaseSecurity[] Securities,
        IReadOnlyDictionary<string, TaseSecurityType> Types)>
        LoadReference(CancellationToken cancellationToken)
    {
        if (CanUseReference())
            return (_securities, _securityTypes);

        await _referenceSync.WaitAsync(cancellationToken);
        try
        {
            if (CanUseReference())
                return (_securities, _securityTypes);

            var types = await SafeClient().GetSecurityTypes(
                cancellationToken);
            var date = DateTime.UtcNow.Date;
            TaseSecurity[] securities = [];
            for (var day = 0;
                day < SecurityLookupDays && securities.Length == 0;
                day++)
            {
                securities = await SafeClient().GetTradedSecurities(
                    date.AddDays(-day),
                    cancellationToken);
            }

            _securities = securities
                .Where(security =>
                    security is not null &&
                    security.SecurityId > 0)
                .GroupBy(security => security.SecurityId)
                .Select(group => group.First())
                .ToArray();
            _securityTypes = types
                .Where(type =>
                    type is not null &&
                    !type.FullTypeCode.IsEmpty())
                .GroupBy(
                    type => type.FullTypeCode,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _referenceLoaded = DateTimeOffset.UtcNow;
            return (_securities, _securityTypes);
        }
        finally
        {
            _referenceSync.Release();
        }
    }

    private bool CanUseReference()
        => _securities is not null &&
            _securityTypes is not null &&
            ReferenceCacheTimeout > TimeSpan.Zero &&
            DateTimeOffset.UtcNow - _referenceLoaded <
                ReferenceCacheTimeout;

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _securities = null;
        _securityTypes = null;
        _referenceLoaded = default;
    }
}
