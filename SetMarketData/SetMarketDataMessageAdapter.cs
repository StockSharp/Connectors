namespace StockSharp.SetMarketData;

public partial class SetMarketDataMessageAdapter
{
    private static readonly HashSet<string> _markets =
        new(["SET", "MAI"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _securityTypes =
        new(
        [
            "CS", "CSF", "PS", "PSF", "W", "TSR",
            "DWC", "DWP", "DR", "ETF", "UT",
        ],
        StringComparer.OrdinalIgnoreCase);

    private SetMarketDataRestClient _client;

    /// <summary>Initializes a new instance.</summary>
    public SetMarketDataMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedMarketDataType(DataType.MarketDepth);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType == DataType.Level1 ||
            dataType == DataType.MarketDepth;

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
        [BoardCodes.Set, SetMarketDataExtensions.IndexBoard];

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
        _ = DataMode.ToApiPath();

        Markets = string.Join(
            ",",
            ValidateCsv(
                Markets,
                _markets,
                nameof(Markets),
                allowEmpty: false)
            .Split(',')
            .Select(value =>
                value.EqualsIgnoreCase("MAI") ? "mai" : "SET"));
        SecurityTypeCodes = ValidateCsv(
            SecurityTypeCodes,
            _securityTypes,
            nameof(SecurityTypeCodes),
            allowEmpty: true);
        IndexSectors = ValidateFreeCsv(
            IndexSectors, nameof(IndexSectors));

        _client = new SetMarketDataRestClient(
            Address,
            Token.UnSecure()?.Trim())
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

    private SetMarketDataRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
    }

    private static string ValidateCsv(
        string value,
        IReadOnlySet<string> allowed,
        string parameterName,
        bool allowEmpty)
    {
        var values = value
            .NormalizeCsv()
            ?.Split(',', StringSplitOptions.RemoveEmptyEntries) ??
            [];
        if (!allowEmpty && values.Length == 0)
        {
            throw new InvalidOperationException(
                $"{parameterName} cannot be empty.");
        }

        var invalid = values.FirstOrDefault(item =>
            !allowed.Contains(item));
        if (!invalid.IsEmpty())
        {
            throw new InvalidOperationException(
                $"{parameterName} contains unsupported value '{invalid}'.");
        }

        return values.Length == 0
            ? null
            : string.Join(",", values);
    }

    private static string ValidateFreeCsv(
        string value,
        string parameterName)
    {
        var normalized = value.NormalizeCsv();
        if (normalized?.Length > 1000 ||
            normalized?.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is ',' or '-' or '&')) == true)
        {
            throw new InvalidOperationException(
                $"{parameterName} contains unsupported characters.");
        }

        return normalized;
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
