namespace StockSharp.VeloData.Native;

sealed class VeloDataRestClient : BaseLogReceiver
{
    private const int _maximumResponseLength = 64 * 1024 * 1024;
    private const int _maximumValues = 22500;

    private readonly HttpClient _client;
    private readonly Uri _newsEndpoint;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _requestInterval;
    private readonly string _apiKey;
    private readonly JsonSerializerSettings _settings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };
    private DateTime _lastRequestTime;
    private bool _isDisposed;

    public VeloDataRestClient(string apiEndpoint, string newsEndpoint,
        SecureString apiKey, TimeSpan requestInterval)
    {
        var apiRoot = ValidateEndpoint(apiEndpoint, "/api/v1/", "market-data");
        _newsEndpoint = ValidateEndpoint(newsEndpoint, "/api/n/", "news");
        if (apiKey.IsEmpty())
            throw new ArgumentNullException(nameof(apiKey));
        if (requestInterval < TimeSpan.Zero ||
            requestInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestInterval));

        _apiKey = apiKey.UnSecure().Trim();
        if (_apiKey.IsEmpty() || _apiKey.Length > 4096 ||
            _apiKey.Any(char.IsControl))
            throw new ArgumentException(
                "Velo Data API key is empty or invalid.", nameof(apiKey));

        _requestInterval = requestInterval;
        _client = new()
        {
            BaseAddress = apiRoot,
            Timeout = TimeSpan.FromSeconds(90),
        };
        _client.DefaultRequestHeaders.Authorization = new(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("api:" + _apiKey)));
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-VeloData/1.0");
    }

    public override string Name => "VeloData_REST";

    public async ValueTask<VeloDataInstrument[]> GetInstrumentsAsync(
        bool isIncludeDelisted, CancellationToken cancellationToken)
    {
        var result = new List<VeloDataInstrument>();
        await AddCatalogAsync(result, VeloDataMarketTypes.Futures, false,
            cancellationToken);
        await AddCatalogAsync(result, VeloDataMarketTypes.Spot, false,
            cancellationToken);
        await AddCatalogAsync(result, VeloDataMarketTypes.Options, false,
            cancellationToken);
        if (isIncludeDelisted)
        {
            await AddCatalogAsync(result, VeloDataMarketTypes.Futures, true,
                cancellationToken);
            await AddCatalogAsync(result, VeloDataMarketTypes.Spot, true,
                cancellationToken);
        }
        return [.. result];
    }

    public async IAsyncEnumerable<VeloDataRow> GetRowsAsync(
        VeloDataRowsRequest value,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateRowsRequest(value);
        var columns = value.Columns.Distinct().ToArray();
        var maximumRows = _maximumValues / columns.Length;
        if (maximumRows <= 0)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Velo Data request contains too many columns.");
        var resolutionMinutes = value.Resolution.ToResolutionMinutes();
        var batchSpan = TimeSpan.FromTicks(checked(
            value.Resolution.Ticks * maximumRows));
        var begin = value.Begin.EnsureUtc();
        var end = value.End.EnsureUtc();

        while (begin < end)
        {
            var batchEnd = AddClamped(begin, batchSpan);
            if (batchEnd > end)
                batchEnd = end;
            var path = BuildRowsPath(value, columns, begin, batchEnd,
                resolutionMinutes);
            var content = await GetBytesAsync(new(path, UriKind.Relative),
                "text/csv", cancellationToken);
            foreach (var row in VeloDataCsv.ParseRows(content))
                yield return row;
            begin = batchEnd;
        }
    }

    public async ValueTask<VeloDataNewsStory[]> GetNewsAsync(DateTime begin,
        CancellationToken cancellationToken)
    {
        begin = begin.EnsureUtc();
        if (begin < DateTime.UnixEpoch)
            begin = DateTime.UnixEpoch;
        var uri = new Uri(_newsEndpoint, "news?begin=" +
            Format(begin.ToVeloMilliseconds()));
        var content = await GetBytesAsync(uri, "application/json",
            cancellationToken);
        if (content.Length == 0)
            return [];
        try
        {
            return JsonConvert.DeserializeObject<VeloDataNewsResponse>(
                Encoding.UTF8.GetString(content), _settings)?.Stories ?? [];
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Velo Data returned invalid news JSON.", error);
        }
    }

    private async ValueTask AddCatalogAsync(List<VeloDataInstrument> target,
        VeloDataMarketTypes marketType, bool isDelisted,
        CancellationToken cancellationToken)
    {
        var path = marketType.ToWire();
        if (marketType != VeloDataMarketTypes.Options)
            path += "?delisted=" + (isDelisted ? "1" : "0");
        var content = await GetBytesAsync(new(path, UriKind.Relative), "text/csv",
            cancellationToken);
        target.AddRange(VeloDataCsv.ParseInstruments(content, marketType));
    }

    private async ValueTask<byte[]> GetBytesAsync(Uri uri, string accept,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnforceRequestIntervalAsync(cancellationToken);
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd(accept);
                HttpResponseMessage response;
                try
                {
                    response = await _client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                }
                catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
                    error is HttpRequestException or TaskCanceledException)
                {
                    if (attempt < 3)
                    {
                        await DelayRetryAsync(attempt, null, cancellationToken);
                        continue;
                    }
                    throw new IOException("Velo Data REST transport failed: " +
                        Redact(error.Message));
                }

                using (response)
                {
                    _lastRequestTime = DateTime.UtcNow;
                    var content = await ReadResponseAsync(response, cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return content;
                    if (attempt < 3 && IsTransient(response.StatusCode))
                    {
                        await DelayRetryAsync(attempt, GetRetryDelay(response),
                            cancellationToken);
                        continue;
                    }
                    throw CreateException(response.StatusCode, content);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask EnforceRequestIntervalAsync(
        CancellationToken cancellationToken)
    {
        var remaining = _requestInterval - (DateTime.UtcNow - _lastRequestTime);
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
    }

    private static async ValueTask<byte[]> ReadResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is long contentLength &&
            contentLength > _maximumResponseLength)
            throw new InvalidDataException("Velo Data response is too large.");
        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;
            if (buffer.Length + read > _maximumResponseLength)
                throw new InvalidDataException("Velo Data response is too large.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private InvalidOperationException CreateException(HttpStatusCode statusCode,
        byte[] content)
    {
        string detail = null;
        if (content.Length > 0)
        {
            try
            {
                var error = JsonConvert.DeserializeObject<VeloDataErrorResponse>(
                    Encoding.UTF8.GetString(content), _settings);
                detail = error?.Message.IsEmpty(error?.Detail).IsEmpty(error?.Error);
            }
            catch (JsonException)
            {
            }
            if (detail.IsEmpty())
            {
                detail = Encoding.UTF8.GetString(content, 0,
                    Math.Min(content.Length, 4096));
            }
        }
        detail = Redact(detail.IsEmpty(statusCode.ToString()));
        return new(
            $"Velo Data REST request failed ({(int)statusCode}): {detail}");
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            statusCode is HttpStatusCode.InternalServerError or
            HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
            HttpStatusCode.GatewayTimeout;

    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
        => response.Headers.RetryAfter?.Delta;

    private static async ValueTask DelayRetryAsync(int attempt,
        TimeSpan? serverDelay, CancellationToken cancellationToken)
    {
        var delay = serverDelay ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromMinutes(1))
            delay = TimeSpan.FromMinutes(1);
        await Task.Delay(delay, cancellationToken);
    }

    private static string BuildRowsPath(VeloDataRowsRequest value,
        VeloDataColumns[] columns, DateTime begin, DateTime end,
        int resolutionMinutes)
    {
        var exchange = Uri.EscapeDataString(VeloDataExtensions.NormalizeVeloIdentifier(
            value.Exchange, nameof(value.Exchange)));
        var product = Uri.EscapeDataString(VeloDataExtensions.NormalizeVeloIdentifier(
            value.Product, nameof(value.Product)));
        var selected = string.Join(",", columns.Select(static column =>
            column.ToWire()));
        return "rows?type=" + value.MarketType.ToWire() +
            "&exchanges=" + exchange + "&products=" + product +
            "&columns=" + selected + "&begin=" +
            Format(begin.ToVeloMilliseconds()) + "&end=" +
            Format(end.ToVeloMilliseconds()) + "&resolution=" +
            Format(resolutionMinutes);
    }

    private static void ValidateRowsRequest(VeloDataRowsRequest value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.MarketType == VeloDataMarketTypes.Unknown)
            throw new ArgumentOutOfRangeException(nameof(value), value.MarketType,
                "Velo Data market type is unknown.");
        _ = VeloDataExtensions.NormalizeVeloIdentifier(value.Exchange,
            nameof(value.Exchange));
        _ = VeloDataExtensions.NormalizeVeloIdentifier(value.Product,
            nameof(value.Product));
        if (value.Columns is not { Length: > 0 } ||
            value.Columns.Any(static column => column is VeloDataColumns.Unknown or
                VeloDataColumns.Time or VeloDataColumns.Exchange or
                VeloDataColumns.Coin or VeloDataColumns.Product or
                VeloDataColumns.Begin or VeloDataColumns.Depth))
            throw new ArgumentException(
                "Velo Data rows request has invalid value columns.", nameof(value));
        _ = value.Resolution.ToResolutionMinutes();
        var begin = value.Begin.EnsureUtc();
        var end = value.End.EnsureUtc();
        if (begin < DateTime.UnixEpoch || begin >= end)
            throw new ArgumentOutOfRangeException(nameof(value),
                "Velo Data rows range must be a positive UTC interval.");
    }

    private static Uri ValidateEndpoint(string endpoint, string expectedSuffix,
        string description)
    {
        if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
            UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() || !uri.Query.IsEmpty() ||
            !uri.Fragment.IsEmpty() ||
            !uri.AbsolutePath.EndsWith(expectedSuffix,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Velo Data {description} endpoint must be an HTTPS '{expectedSuffix}' root without credentials, query, or fragment.",
                nameof(endpoint));
        return uri;
    }

    private static DateTime AddClamped(DateTime value, TimeSpan interval)
    {
        value = value.EnsureUtc();
        var ticks = Math.Min(interval.Ticks, DateTime.MaxValue.Ticks - value.Ticks);
        return new(value.Ticks + ticks, DateTimeKind.Utc);
    }

    private static string Format(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    private static string Format(int value)
        => value.ToString(CultureInfo.InvariantCulture);

    private string Redact(string value)
        => value.IsEmpty()
            ? value
            : value.Replace(_apiKey, "***", StringComparison.Ordinal);

    protected override void DisposeManaged()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _client.Dispose();
        _gate.Dispose();
        base.DisposeManaged();
    }
}
