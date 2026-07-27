namespace StockSharp.Edinet;

public partial class EdinetMessageAdapter
{
    private readonly SemaphoreSlim _companiesLock = new(1, 1);
    private EdinetRestClient _client;
    private EdinetCompany[] _companies;
    private DateTimeOffset _companiesLoadedAt;

    /// <summary>Initializes a new instance.</summary>
    public EdinetMessageAdapter(
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
        [BoardCodes.Tse, "EDINET"];

    /// <summary>
    /// Download an EDINET filing as PDF, XBRL ZIP, attachment ZIP,
    /// English ZIP, or converted CSV ZIP.
    /// </summary>
    public Task<byte[]> DownloadDocumentAsync(
        string documentId,
        EdinetDocumentFormats format,
        CancellationToken cancellationToken = default)
        => SafeClient().DownloadDocument(
            documentId, format, cancellationToken);

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
            CodeListAddress, nameof(CodeListAddress));
        ValidateHttpsAddress(
            ViewerAddress, nameof(ViewerAddress));
        _ = DisclosureType.ToDocumentTypeCodes();

        if (DefaultLookupDays is < 1 or > 3660)
        {
            throw new InvalidOperationException(
                "EDINET default lookup days must be from 1 to 3660.");
        }
        if (MaxDays is < 1 or > 3660)
        {
            throw new InvalidOperationException(
                "EDINET maximum days must be from 1 to 3660.");
        }
        if (DefaultLookupDays > MaxDays)
        {
            throw new InvalidOperationException(
                "EDINET default lookup days cannot exceed maximum days.");
        }
        if (RequestInterval < TimeSpan.Zero ||
            RequestInterval > TimeSpan.FromMinutes(1))
        {
            throw new InvalidOperationException(
                "EDINET request interval must be from zero to one minute.");
        }
        if (MaxDocumentSizeMb is < 1 or > 2047)
        {
            throw new InvalidOperationException(
                "EDINET maximum document size must be from 1 to 2047 MB.");
        }
        if (CodeListCacheTimeout < TimeSpan.Zero ||
            CodeListCacheTimeout > TimeSpan.FromDays(30))
        {
            throw new InvalidOperationException(
                "EDINET company cache lifetime must be from zero to 30 days.");
        }

        _client = new EdinetRestClient(
            Address,
            Token.UnSecure()?.Trim(),
            CodeListAddress,
            MaxDocumentSizeMb)
        {
            Parent = this,
        };
        ClearCompanies();

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

    private EdinetRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private async Task<EdinetCompany[]> LoadCompanies(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_companies is not null &&
            now - _companiesLoadedAt < CodeListCacheTimeout)
        {
            return _companies;
        }

        await _companiesLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTimeOffset.UtcNow;
            if (_companies is null ||
                now - _companiesLoadedAt >=
                    CodeListCacheTimeout)
            {
                _companies = (await SafeClient().GetCompanies(
                        cancellationToken))
                    .Where(company =>
                        company.EdinetCode.IsEdinetCode())
                    .GroupBy(
                        company => company.EdinetCode,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderBy(company => company.EdinetCode)
                    .ToArray();
                _companiesLoadedAt = now;
            }

            return _companies;
        }
        finally
        {
            _companiesLock.Release();
        }
    }

    private async Task<EdinetCompany> ResolveCompany(
        SecurityId securityId,
        CancellationToken cancellationToken)
    {
        var identity = securityId.GetEdinetIdentity();
        var companies = await LoadCompanies(cancellationToken);
        var company = companies.FirstOrDefault(item =>
            item.EdinetCode.EqualsIgnoreCase(identity) ||
            item.SecuritiesCode.EqualsIgnoreCase(identity) ||
            item.SecuritiesCode
                .ToEdinetTickerOrNull()
                .EqualsIgnoreCase(identity));

        if (company is null)
        {
            throw new InvalidOperationException(
                $"EDINET company '{identity}' was not found in the company-code list.");
        }

        return company;
    }

    private void ClearCompanies()
    {
        _companies = null;
        _companiesLoadedAt = default;
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        ClearCompanies();
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
