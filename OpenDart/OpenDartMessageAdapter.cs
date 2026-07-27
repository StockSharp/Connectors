namespace StockSharp.OpenDart;

public partial class OpenDartMessageAdapter
{
    private static readonly string[] _indicatorCategories =
        ["M210000", "M220000", "M230000", "M240000"];

    private readonly SemaphoreSlim _companiesLock = new(1, 1);
    private OpenDartRestClient _client;
    private OpenDartCompanyCode[] _companies;

    /// <summary>Initializes a new instance.</summary>
    public OpenDartMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.News);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Level1 ||
            dataType == DataType.News;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.Krx];

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

        var token = Token.UnSecure()?.Trim();
        if (token?.Length != 40)
        {
            throw new InvalidOperationException(
                "Open DART API authentication key must contain 40 characters.");
        }
        ValidateHttpsAddress(Address, nameof(Address));
        ValidateHttpsAddress(
            DisclosureAddress, nameof(DisclosureAddress));
        _ = DisclosureType.ToApiCode();
        _ = CorporationClass.ToApiCode();
        _ = ReportType.ToApiCode();

        var currentYear = OpenDartExtensions.KoreaToday().Year;
        if (BusinessYear is int year &&
            (year < 2022 || year > currentYear + 1))
        {
            throw new InvalidOperationException(
                $"Open DART business year must be from 2022 to {currentYear + 1}.");
        }
        if (FinancialSearchYears is < 1 or > 20)
        {
            throw new InvalidOperationException(
                "Open DART financial search years must be from 1 to 20.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "Open DART maximum news pages must be from 1 to 1000.");
        }

        _client = new OpenDartRestClient(Address, token)
        {
            Parent = this,
        };
        _companies = null;

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

    private OpenDartRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private async Task<OpenDartCompanyCode[]> LoadCompanies(
        CancellationToken cancellationToken)
    {
        if (_companies is not null)
            return _companies;

        await _companiesLock.WaitAsync(cancellationToken);
        try
        {
            if (_companies is null)
            {
                _companies = (await SafeClient().GetCompanies(
                        cancellationToken))
                    .Where(company =>
                        company.StockCode.IsStockCode() &&
                        company.CorporationCode.IsCorporationCode())
                    .GroupBy(
                        company => company.StockCode,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(
                            company => company.ModifiedDate)
                        .First())
                    .OrderBy(company => company.StockCode)
                    .ToArray();
            }

            return _companies;
        }
        finally
        {
            _companiesLock.Release();
        }
    }

    private async Task<OpenDartCompanyCode> ResolveCompany(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var corporationCode = securityId.GetCorporationCode();
        var code = securityId.SecurityCode?.Trim();
        var companies = await LoadCompanies(cancellationToken);

        var company = !corporationCode.IsEmpty()
            ? companies.FirstOrDefault(item =>
                item.CorporationCode.EqualsIgnoreCase(
                    corporationCode))
            : companies.FirstOrDefault(item =>
                item.StockCode.EqualsIgnoreCase(code) ||
                item.CorporationCode.EqualsIgnoreCase(code));

        if (company is null)
        {
            throw new InvalidOperationException(
                $"Open DART company '{corporationCode.IsEmpty(code)}' was not found in the listed-company registry.");
        }

        return company;
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _companies = null;
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
