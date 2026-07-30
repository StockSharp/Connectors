namespace StockSharp.Marketaux;

public partial class MarketauxMessageAdapter
{
    private MarketauxRestClient _client;
    private string _languages;
    private string _entityTypes;
    private string _countries;

    /// <summary>Initializes a new instance.</summary>
    public MarketauxMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.News);

        foreach (var dataType in MarketauxDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => false;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [MarketauxExtensions.DefaultBoard];

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
                "Marketaux address must be an absolute HTTPS URI.");
        }
        if (NewsPageSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Marketaux news page size must be from 1 to 1000.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Marketaux page limit must be from 1 to 1000.");
        }
        if (DatasetLimit is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Marketaux dataset limit must be from 1 to 1000.");
        }
        if (!Enum.IsDefined(SentimentInterval))
        {
            throw new InvalidOperationException(
                "Marketaux sentiment interval is invalid.");
        }

        _languages = MarketauxExtensions.NormalizeCsv(
            Languages, "languages");
        _entityTypes = MarketauxExtensions.NormalizeCsv(
            EntityTypes, "entity types");
        _countries = MarketauxExtensions.NormalizeCsv(
            Countries, "countries");
        _client = new MarketauxRestClient(
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

    private MarketauxRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string SafeLanguages()
        => _languages ??
            MarketauxExtensions.NormalizeCsv(
                Languages, "languages");

    private string SafeEntityTypes()
        => _entityTypes ??
            MarketauxExtensions.NormalizeCsv(
                EntityTypes, "entity types");

    private string SafeCountries()
        => _countries ??
            MarketauxExtensions.NormalizeCsv(
                Countries, "countries");

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _languages = null;
        _entityTypes = null;
        _countries = null;
    }
}
