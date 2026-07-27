namespace StockSharp.Bavest;

public partial class BavestMessageAdapter
{
    private BavestRestClient _client;
    private string _currency;
    private string _exchange;
    private string _exchangeCode;
    private string _screenerQuery;

    /// <summary>Initializes a new instance.</summary>
    public BavestMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
        this.AddSupportedCandleTimeFrames(
            BavestExtensions.TimeFrames);
        foreach (var dataType in BavestDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BavestExtensions.DefaultBoard];

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
                "Bavest address must be an absolute HTTPS URI.");
        }
        if (!Enum.IsDefined(FinancialFrequency))
        {
            throw new InvalidOperationException(
                "Bavest financial frequency is invalid.");
        }
        if (PageSize is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Bavest page size must be from 1 to 1000.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Bavest page limit must be from 1 to 1000.");
        }
        if (NewsLimit is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Bavest news limit must be from 1 to 100.");
        }
        if (DatasetLimit is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Bavest dataset limit must be from 1 to 1000.");
        }

        _currency = BavestExtensions.NormalizeOptionalCode(
            Currency, "currency");
        _exchange = BavestExtensions.NormalizeOptionalCode(
            Exchange, "exchange");
        _exchangeCode = BavestExtensions.NormalizeOptionalCode(
            ExchangeCode, "exchange code");
        _screenerQuery = NormalizeScreenerQuery(ScreenerQuery);
        _client = new BavestRestClient(
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

    private BavestRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string SafeCurrency()
        => _currency ??
            BavestExtensions.NormalizeOptionalCode(
                Currency, "currency");

    private string SafeExchange()
        => _exchange ??
            BavestExtensions.NormalizeOptionalCode(
                Exchange, "exchange");

    private string SafeExchangeCode()
        => _exchangeCode ??
            BavestExtensions.NormalizeOptionalCode(
                ExchangeCode, "exchange code");

    private string SafeScreenerQuery()
        => _screenerQuery ??
            NormalizeScreenerQuery(ScreenerQuery);

    private static string NormalizeScreenerQuery(string value)
    {
        try
        {
            return (value.IsEmpty()
                ? new JArray()
                : JArray.Parse(value))
                .ToString(Formatting.None);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                "Bavest screener query must be a JSON array.");
        }
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _currency = null;
        _exchange = null;
        _exchangeCode = null;
        _screenerQuery = null;
    }
}
