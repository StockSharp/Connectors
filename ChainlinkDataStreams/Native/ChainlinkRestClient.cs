namespace StockSharp.ChainlinkDataStreams.Native;

sealed class ChainlinkRestClient : BaseLogReceiver
{
    private const int _maximumResponseLength = 64 * 1024 * 1024;
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);
    private readonly Uri _endpoint;
    private readonly HttpClient _client;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerSettings _settings = new()
    {
        Culture = CultureInfo.InvariantCulture,
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        Formatting = Formatting.None,
        NullValueHandling = NullValueHandling.Ignore,
    };
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly TimeSpan _requestInterval;
    private DateTime _lastRequestTime;
    private bool _isDisposed;

    public ChainlinkRestClient(string endpoint, SecureString key,
        SecureString secret, TimeSpan requestInterval)
    {
        _endpoint = ChainlinkExtensions.ValidateRestEndpoint(endpoint);
        if (key.IsEmpty())
            throw new ArgumentNullException(nameof(key));
        if (secret.IsEmpty())
            throw new ArgumentNullException(nameof(secret));
        _apiKey = ValidateCredential(key.UnSecure(), "API key", nameof(key));
        _apiSecret = ValidateCredential(secret.UnSecure(), "API secret",
            nameof(secret));
        if (requestInterval < TimeSpan.Zero ||
            requestInterval > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(requestInterval));
        _requestInterval = requestInterval;
        _client = new()
        {
            BaseAddress = _endpoint,
            Timeout = TimeSpan.FromSeconds(90),
        };
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-ChainlinkDataStreams/1.0");
    }

    public override string Name => "Chainlink_Data_Streams_REST";

    public async ValueTask<ChainlinkFeed[]> GetFeedsAsync(
        CancellationToken cancellationToken)
    {
        var response = await GetAsync<ChainlinkFeedsResponse>("api/v1/feeds",
            cancellationToken);
        return response?.Feeds ?? throw new InvalidDataException(
            "Chainlink returned no feeds collection.");
    }

    public async ValueTask<ChainlinkReportEnvelope> GetLatestReportAsync(
        string feedId, CancellationToken cancellationToken)
    {
        feedId = feedId.ParseFeed().FeedId;
        var response = await GetAsync<ChainlinkSingleReportResponse>(
            "api/v1/reports/latest?feedID=" + Uri.EscapeDataString(feedId),
            cancellationToken);
        return response?.Report ?? throw new InvalidDataException(
            "Chainlink returned no latest report.");
    }

    public async ValueTask<ChainlinkReportEnvelope[]> GetReportPageAsync(
        string feedId, long startTimestamp, int limit,
        CancellationToken cancellationToken)
    {
        feedId = feedId.ParseFeed().FeedId;
        if (startTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(startTimestamp));
        if (limit is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(limit));
        var path = "api/v1/reports/page?feedID=" +
            Uri.EscapeDataString(feedId) + "&limit=" +
            limit.ToString(CultureInfo.InvariantCulture) + "&startTimestamp=" +
            startTimestamp.ToString(CultureInfo.InvariantCulture);
        var response = await GetAsync<ChainlinkReportsResponse>(path,
            cancellationToken);
        return response?.Reports ?? throw new InvalidDataException(
            "Chainlink returned no reports collection.");
    }

    public async ValueTask<string[]> GetAvailableOriginsAsync(
        string webSocketEndpoint, CancellationToken cancellationToken)
    {
        var webSocketUri = ChainlinkExtensions.ValidateWebSocketEndpoint(
            webSocketEndpoint);
        var builder = new UriBuilder(webSocketUri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = webSocketUri.IsDefaultPort ? -1 : webSocketUri.Port,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnforceRequestIntervalAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Head, builder.Uri);
            ApplyAuthentication(request);
            using var response = await _client.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _lastRequestTime = DateTime.UtcNow;
            if (!response.IsSuccessStatusCode)
            {
                var content = await ReadResponseAsync(response, cancellationToken);
                throw CreateException(response.StatusCode, content);
            }
            if (!response.Headers.TryGetValues("X-Cll-Available-Origins",
                out var values))
                return [];
            var text = string.Join(",", values).Trim().Trim('{', '}');
            if (text.IsEmpty())
                return [];
            var result = text.Split(',', StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
                .Where(IsValidOrigin)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (result.Length > 16)
                throw new InvalidDataException(
                    "Chainlink returned too many WebSocket origins.");
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private ValueTask<T> GetAsync<T>(string path,
        CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, path, cancellationToken);

    private async ValueTask<T> SendAsync<T>(HttpMethod method, string path,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (!Uri.TryCreate(_endpoint, path, out var requestUri) ||
            !requestUri.Host.Equals(_endpoint.Host,
                StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Chainlink request path is invalid.",
                nameof(path));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await EnforceRequestIntervalAsync(cancellationToken);
                using var request = new HttpRequestMessage(method, requestUri);
                ApplyAuthentication(request);
                HttpResponseMessage response;
                try
                {
                    response = await _client.SendAsync(request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken);
                }
                catch (Exception error) when (
                    !cancellationToken.IsCancellationRequested &&
                    error is HttpRequestException or TaskCanceledException)
                {
                    if (attempt < 3)
                    {
                        await DelayRetryAsync(attempt, null, cancellationToken);
                        continue;
                    }
                    throw new IOException(
                        "Chainlink REST transport failed: " + Redact(error.Message));
                }

                using (response)
                {
                    _lastRequestTime = DateTime.UtcNow;
                    var content = await ReadResponseAsync(response,
                        cancellationToken);
                    if (response.IsSuccessStatusCode)
                        return Deserialize<T>(content);
                    if (attempt < 3 && IsTransient(response.StatusCode))
                    {
                        await DelayRetryAsync(attempt,
                            response.Headers.RetryAfter?.Delta,
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

    private void ApplyAuthentication(HttpRequestMessage request)
    {
        var authentication = ChainlinkAuthenticator.Create(request.Method,
            request.RequestUri, ReadOnlySpan<byte>.Empty, _apiKey, _apiSecret,
            DateTime.UtcNow);
        ChainlinkAuthenticator.Apply(request.Headers, authentication);
    }

    private T Deserialize<T>(byte[] content)
    {
        if (content.Length == 0)
            throw new InvalidDataException("Chainlink returned an empty response.");
        try
        {
            return JsonConvert.DeserializeObject<T>(_strictUtf8.GetString(content),
                _settings);
        }
        catch (Exception error) when (error is JsonException or
            DecoderFallbackException)
        {
            throw new InvalidDataException("Chainlink returned invalid JSON.", error);
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
            throw new InvalidDataException("Chainlink response is too large.");
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
                throw new InvalidDataException("Chainlink response is too large.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private InvalidOperationException CreateException(HttpStatusCode statusCode,
        byte[] content)
    {
        var detail = string.Empty;
        if (content.Length > 0)
        {
            try
            {
                var text = _strictUtf8.GetString(content);
                var error = JsonConvert.DeserializeObject<ChainlinkErrorResponse>(text,
                    _settings);
                detail = error?.Message.IsEmpty(error?.Error).IsEmpty(error?.Detail)
                    .IsEmpty(text);
            }
            catch (Exception error) when (error is JsonException or
                DecoderFallbackException)
            {
                detail = "non-JSON error response";
            }
        }
        return new InvalidOperationException(
            $"Chainlink REST request failed with HTTP {(int)statusCode}: " +
            Redact(detail).IsEmpty("no error details"));
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 || (int)statusCode >= 500;

    private static async ValueTask DelayRetryAsync(int attempt,
        TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var delay = retryAfter is TimeSpan value && value > TimeSpan.Zero &&
            value <= TimeSpan.FromMinutes(1)
            ? value
            : TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt + 1, 5)));
        await Task.Delay(delay, cancellationToken);
    }

    private string Redact(string value)
        => value.IsEmpty() ? value : value
            .Replace(_apiKey, "***", StringComparison.Ordinal)
            .Replace(_apiSecret, "***", StringComparison.Ordinal);

    private static bool IsValidOrigin(string value)
        => !value.IsEmpty() && value.Length <= 512 &&
            !value.Any(char.IsControl);

    private static string ValidateCredential(string value, string name,
        string parameterName)
    {
        value = value?.Trim();
        if (value.IsEmpty() || value.Length > 8192 || value.Any(char.IsControl))
            throw new ArgumentException($"Chainlink {name} is empty or invalid.",
                parameterName);
        return value;
    }

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
