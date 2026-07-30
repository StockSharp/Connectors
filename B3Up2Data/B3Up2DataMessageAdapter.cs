namespace StockSharp.B3Up2Data;

public partial class B3Up2DataMessageAdapter
{
    private const int _maxCertificateSize = 12 * 1024 * 1024;

    private B3Up2DataRestClient _client;
    private string _selectedChannel;
    private string _blobPrefix;

    /// <summary>Initializes a new instance.</summary>
    public B3Up2DataMessageAdapter(
        IdGenerator transactionIdGenerator)
        : base(transactionIdGenerator)
    {
        this.AddMarketDataSupport();
        this.AddSupportedMarketDataType(DataType.Level1);
        this.AddSupportedCandleTimeFrames(
            B3Up2DataExtensions.TimeFrames);

        foreach (var dataType in B3Up2DataDataTypes.All)
            this.AddSupportedMarketDataType(dataType);
    }

    /// <inheritdoc />
    public override bool IsAllDownloadingSupported(DataType dataType)
        => dataType == DataType.Securities ||
            dataType.IsTFCandles ||
            B3Up2DataDataTypes.TryGetKind(dataType, out _);

    /// <inheritdoc />
    public override string[] AssociatedBoards { get; } =
    [
        B3Up2DataExtensions.EquitiesBoard,
        B3Up2DataExtensions.IndexBoard,
    ];

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
        ValidateSettings();
        _blobPrefix =
            B3Up2DataExtensions.NormalizeBlobPrefix(BlobPrefix);
        _client = new B3Up2DataRestClient(Address)
        {
            Parent = this,
        };

        try
        {
            var existingSas = SasUri.UnSecure();
            if (!existingSas.IsEmpty())
            {
                _client.SetSas(existingSas);
                _selectedChannel = ChannelName
                    .IsEmpty("Direct SAS");
            }
            else
            {
                var certificate = await LoadCertificate(
                    CertificatePath, cancellationToken);
                var token = await _client.GetAccessToken(
                    certificate,
                    CertificatePassword.UnSecure(),
                    Key.UnSecure(),
                    Secret.UnSecure(),
                    cancellationToken);
                var channels = await _client.GenerateSas(
                    token.AccessToken,
                    certificate,
                    CertificatePassword.UnSecure(),
                    cancellationToken);
                var selected = SelectChannel(
                    channels, ChannelName);
                _client.SetSas(selected.Sas);
                _selectedChannel = selected.Name;
            }

            await base.ConnectAsync(
                connectMsg, cancellationToken);
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

    private void ValidateSettings()
    {
        if (Address is null ||
            !Address.IsAbsoluteUri ||
            Address.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA address must be an absolute HTTPS URI.");
        }
        if (!Enum.IsDefined(FileFormat))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA file format is invalid.");
        }
        if (LookbackDays is < 1 or > 31)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA lookback must be from 1 to 31 days.");
        }
        if (PageSize is < 1 or > 5000)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA list page size must be from 1 to 5000.");
        }
        if (MaxPages is < 1 or > 1000)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA list page limit must be from 1 to 1000.");
        }
        if (MaxRawFiles is < 1 or > 10000)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA raw file limit must be from 1 to 10000.");
        }

        if (!SasUri.IsEmpty())
            return;
        if (Key.IsEmpty())
        {
            throw new InvalidOperationException(
                "B3 UP2DATA client ID is not specified.");
        }
        if (Secret.IsEmpty())
        {
            throw new InvalidOperationException(
                "B3 UP2DATA client secret is not specified.");
        }
        if (CertificatePath.IsEmpty())
        {
            throw new InvalidOperationException(
                "B3 UP2DATA certificate path is not specified.");
        }
        if (CertificatePassword.IsEmpty())
        {
            throw new InvalidOperationException(
                "B3 UP2DATA certificate password is not specified.");
        }
        if (ChannelName.IsEmpty())
        {
            throw new InvalidOperationException(
                "B3 UP2DATA SAS channel name is not specified.");
        }
    }

    private static async Task<string> LoadCertificate(
        string path,
        CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                "B3 UP2DATA PFX certificate was not found.",
                path);
        }
        if (info.Length is <= 0 or > _maxCertificateSize)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA PFX certificate size is invalid.");
        }
        var bytes = await File.ReadAllBytesAsync(
            path, cancellationToken);
        return Convert.ToBase64String(bytes);
    }

    private static B3SasChannel SelectChannel(
        IEnumerable<B3SasChannel> channels,
        string filter)
    {
        var values = channels?.ToArray() ?? [];
        filter = filter?.Trim();
        var selected = values.FirstOrDefault(channel =>
            channel.Name?.Contains(
                filter,
                StringComparison.OrdinalIgnoreCase) == true);
        if (selected is not null)
            return selected;

        var available = values
            .Select(channel => channel.Name)
            .Where(name => !name.IsEmpty())
            .Take(20)
            .JoinComma();
        throw new InvalidOperationException(
            $"B3 UP2DATA SAS channel '{filter}' was not found. " +
            $"Available channels: {available.IsEmpty("none")}.");
    }

    private B3Up2DataRestClient SafeClient()
        => _client ?? throw new InvalidOperationException(
            LocalizedStrings.ConnectionNotOk);

    private string SafeChannel()
        => _selectedChannel.IsEmpty(
            ChannelName.IsEmpty("UP2DATA"));

    private string SafeBlobPrefix()
        => _blobPrefix ??
            B3Up2DataExtensions.NormalizeBlobPrefix(BlobPrefix);

    private void RequireCsv()
    {
        if (FileFormat != B3Up2DataFileFormats.Csv)
        {
            throw new NotSupportedException(
                "B3 UP2DATA normal securities, Level1, and candles " +
                "require the CSV file format.");
        }
    }

    private async Task<B3BlobItem[]> ListAll(
        string prefix,
        int maxItems,
        CancellationToken cancellationToken)
    {
        if (maxItems <= 0)
            return [];
        var result = new List<B3BlobItem>();
        var markers = new HashSet<string>(
            StringComparer.Ordinal);
        string marker = null;

        for (var page = 0;
            page < MaxPages && result.Count < maxItems;
            page++)
        {
            var response = await SafeClient().ListBlobs(
                prefix,
                marker,
                Math.Min(PageSize, maxItems - result.Count),
                cancellationToken);
            result.AddRange(
                (response.Items ?? [])
                    .Where(item => item is not null)
                    .Take(maxItems - result.Count));
            marker = response.NextMarker;
            if (marker.IsEmpty())
                break;
            if (!markers.Add(marker))
            {
                throw new InvalidOperationException(
                    "B3 UP2DATA Azure listing repeated a continuation marker.");
            }
        }

        return [.. result];
    }

    private async Task<B3BlobItem[]> ListDataset(
        B3Up2DataDataKinds kind,
        DateTime date,
        int maxLogicalFiles,
        CancellationToken cancellationToken)
    {
        var descriptor = kind.ToDescriptor();
        var prefix = descriptor.BuildPrefix(date);
        var scanLimit = checked(Math.Min(
            Math.Max(maxLogicalFiles, 1) * 50,
            100000));
        var extension = FileFormat.ToExtension();
        var listed = await ListAll(
            prefix, scanLimit, cancellationToken);
        return listed
            .Where(item =>
                item.Name.EndsWith(
                    extension,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                item => item.GetLogicalName(date),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item =>
                    item.GetRevision(date))
                .ThenByDescending(item => item.LastModified)
                .First())
            .OrderBy(item => item.Name)
            .Take(maxLogicalFiles)
            .ToArray();
    }

    private async Task<(DateTime Date, B3BlobItem Blob)?>
        FindLatest(
            B3Up2DataDataKinds kind,
            DateTime asOf,
            CancellationToken cancellationToken)
    {
        asOf = asOf.ToUtcDate();

        for (var offset = 0; offset < LookbackDays; offset++)
        {
            var date = asOf.AddDays(-offset);
            var blobs = await ListDataset(
                kind, date, 1, cancellationToken);
            if (blobs.Length > 0)
                return (date, blobs[0]);
        }

        return null;
    }

    private async Task<(DateTime Date, B3BlobItem Blob,
        B3DownloadedBlob Download)> DownloadLatest(
        B3Up2DataDataKinds kind,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        var found = await FindLatest(
            kind, asOf, cancellationToken);
        if (found is null)
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA did not contain a {kind} file in the " +
                $"last {LookbackDays} days.");
        }
        var download = await SafeClient().DownloadBlob(
            found.Value.Blob.Name, cancellationToken);
        return (
            found.Value.Date,
            found.Value.Blob,
            download);
    }

    private async Task<B3CsvTable> DownloadLatestCsv(
        B3Up2DataDataKinds kind,
        DateTime asOf,
        CancellationToken cancellationToken)
    {
        RequireCsv();
        var result = await DownloadLatest(
            kind, asOf, cancellationToken);
        return B3CsvTable.Parse(result.Download.Content);
    }

    private void DisposeClient()
    {
        _client?.Dispose();
        _client = null;
        _selectedChannel = null;
        _blobPrefix = null;
    }
}
