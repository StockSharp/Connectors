namespace StockSharp.EsmaFirds;

public partial class EsmaFirdsMessageAdapter
{
    private EsmaFirdsRestClient _client;
    private string[] _cfiCategories;

    /// <summary>Initializes a new instance.</summary>
    public EsmaFirdsMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => false;

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
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "ESMA FIRDS address must be an absolute HTTPS URI.");
        }
        if (MaxResults is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "ESMA FIRDS maximum results must be from 1 to 1000.");
        }

        _cfiCategories = ParseCfiCategories(CfiCategories);
        _client = new EsmaFirdsRestClient(Address)
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

    private EsmaFirdsRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    internal static string[] ParseCfiCategories(string value)
    {
        if (value.IsEmpty())
            return [];

        var categories = value
            .Split(
                ',',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(category => category.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (categories.Length > 8 ||
            categories.Any(category =>
                category.Length != 1 ||
                !char.IsAsciiLetterUpper(category[0])))
        {
            throw new InvalidOperationException(
                "ESMA FIRDS CFI categories must be a comma-separated " +
                "list of at most eight letters.");
        }

        return categories;
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _cfiCategories = null;
    }
}
