namespace StockSharp.NDAX.Native;

sealed class NDAXRestClient : BaseLogReceiver
{
    private sealed class RateGate : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private DateTime _nextRequest;

        public async ValueTask WaitAsync(CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var delay = _nextRequest - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                _nextRequest = DateTime.UtcNow +
                    TimeSpan.FromMilliseconds(1200);
            }
            finally
            {
                _sync.Release();
            }
        }

        public void Dispose() => _sync.Dispose();
    }

    private const int _maximumAttempts = 4;
    private const int _maximumResponseBytes = 8 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly RateGate _rateGate = new();
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.DateTime,
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public NDAXRestClient(string endpoint)
    {
        _endpoint = ValidateEndpoint(endpoint);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-NDAX-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "NDAX_REST";

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _rateGate.Dispose();
        base.DisposeManaged();
    }

    public ValueTask<NdaxInstrument[]> GetInstrumentsAsync(int omsId,
        CancellationToken cancellationToken)
        => GetAsync<NdaxInstrument[]>(
            $"GetInstruments?OMSId={omsId}", cancellationToken);

    public ValueTask<NdaxProduct[]> GetProductsAsync(int omsId,
        CancellationToken cancellationToken)
        => GetAsync<NdaxProduct[]>($"GetProducts?OMSId={omsId}",
            cancellationToken);

    public ValueTask<NdaxLevel1> GetLevel1Async(int omsId, int instrumentId,
        CancellationToken cancellationToken)
        => GetAsync<NdaxLevel1>($"GetLevel1?OMSId={omsId}&InstrumentId=" +
            instrumentId.ToString(CultureInfo.InvariantCulture),
            cancellationToken);

    public ValueTask<NdaxLevel2Entry[]> GetLevel2Async(int omsId,
        int instrumentId, int depth, CancellationToken cancellationToken)
        => GetAsync<NdaxLevel2Entry[]>(
            $"GetL2Snapshot?OMSId={omsId}&InstrumentId={instrumentId}" +
            $"&Depth={depth.Min(500).Max(1)}", cancellationToken);

    public ValueTask<NdaxCandle[]> GetCandlesAsync(int omsId,
        int instrumentId, TimeSpan timeFrame, DateTime from, DateTime to,
        CancellationToken cancellationToken)
        => GetAsync<NdaxCandle[]>(
            $"GetTickerHistory?OMSId={omsId}&InstrumentId={instrumentId}" +
            $"&Interval={timeFrame.ToInterval()}&FromDate=" +
            from.ToUtcTime().ToString("O",
                CultureInfo.InvariantCulture).EscapeQuery() + "&ToDate=" +
            to.ToUtcTime().ToString("O",
                CultureInfo.InvariantCulture).EscapeQuery(),
            cancellationToken);

    public ValueTask<NdaxRecentTrade[]> GetTradesAsync(string symbol,
        CancellationToken cancellationToken)
        => GetAsync<NdaxRecentTrade[]>("Trades?market_pair=" +
            symbol.NormalizeSymbol().EscapeQuery(), cancellationToken);

    private async ValueTask<TResponse> GetAsync<TResponse>(string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _rateGate.WaitAsync(cancellationToken);
            using var response = await _http.GetAsync(
                new Uri(_endpoint, path), HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            var body = await ReadBodyAsync(response.Content, cancellationToken);
            if (attempt < _maximumAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new NdaxApiException(path, (int)response.StatusCode,
                    BodyMessage(body));
            return Deserialize<TResponse>(path, body);
        }
    }

    private TResponse Deserialize<TResponse>(string operation, string body)
    {
        if (body.IsEmpty())
            throw new InvalidDataException(
                $"NDAX {operation} returned an empty response.");
        try
        {
            if (body.TrimStart().StartsWith('{'))
            {
                var generic = JsonConvert.DeserializeObject<
                    NdaxGenericResponse>(body, _jsonSettings);
                if (generic is { Result: false } &&
                    (!generic.ErrorMessage.IsEmpty() ||
                        generic.ErrorCode != 0))
                    throw new NdaxApiException(operation, generic.ErrorCode,
                        generic.ErrorMessage ?? generic.Detail ??
                            "operation failed");
            }
            return JsonConvert.DeserializeObject<TResponse>(body,
                _jsonSettings) ?? throw new InvalidDataException(
                $"NDAX {operation} returned an empty JSON value.");
        }
        catch (NdaxApiException)
        {
            throw;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"NDAX {operation} returned an unexpected response shape.",
                error);
        }
    }

    private static Uri ValidateEndpoint(string value)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                "NDAX REST endpoint must be an absolute HTTPS URI.",
                nameof(value));
        return endpoint;
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode is (HttpStatusCode)429 || (int)statusCode >= 500;

    private static string BodyMessage(string body)
    {
        body = body?.Trim();
        if (body?.Length > 512)
            body = body[..512];
        return body.IsEmpty() ? "request failed" : body;
    }

    private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
        int attempt, CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ??
            TimeSpan.FromMilliseconds(500 * (1 << attempt));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromSeconds(10))
            delay = TimeSpan.FromSeconds(10);
        await Task.Delay(delay, cancellationToken);
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "NDAX response exceeds the 8 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "NDAX response exceeds the 8 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
