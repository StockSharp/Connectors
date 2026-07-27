namespace StockSharp.SetMarketData.Native;

sealed class SetMarketDataApiException : InvalidOperationException
{
    public SetMarketDataApiException(
        string status,
        string message)
        : base(message)
    {
        Status = status;
    }

    public string Status { get; }
}

sealed class SetMarketDataRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 64 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _apiKey;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public SetMarketDataRestClient(
        Uri address,
        string apiKey,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-SetMarketData/1.0");
        _delay = delay ?? Task.Delay;
    }

    public Task<SetStockQuote[]> GetStocks(
        SetMarketDataModes mode,
        SetStockQuery query,
        CancellationToken cancellationToken)
        => GetArray<SetStockQuote>(
            $"{mode.ToApiPath()}/stock",
            new Dictionary<string, string>
            {
                ["market"] = query.Markets,
                ["indexSector"] = query.IndexSectors,
                ["securityType"] = query.SecurityTypes,
                ["stockSymbol"] = query.Symbols,
                ["oddLotFlag"] = query.OddLots
                    ? "true"
                    : null,
            },
            "stock quotation",
            cancellationToken);

    public Task<SetIndexQuote[]> GetIndices(
        SetMarketDataModes mode,
        SetIndexQuery query,
        CancellationToken cancellationToken)
        => GetArray<SetIndexQuote>(
            $"{mode.ToApiPath()}/index",
            new Dictionary<string, string>
            {
                ["market"] = query.Markets,
                ["indexSector"] = query.IndexSectors,
            },
            "index quotation",
            cancellationToken);

    private async Task<T[]> GetArray<T>(
        string path,
        IReadOnlyDictionary<string, string> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var address = BuildAddress(path, query);
        var payload = await GetPayload(
            address, operation, cancellationToken);

        JToken root;
        try
        {
            root = JToken.Parse(
                Encoding.UTF8.GetString(payload));
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"SET returned invalid JSON for {operation}.");
        }

        var error = GetApiError(root, operation);
        if (error is not null)
            throw error;

        JArray array = root as JArray;
        if (array is null && root is JObject obj)
        {
            if (obj["symbol"]?.ToString().IsEmpty() == false)
                array = new JArray(obj);
            else
                array = obj.Properties()
                    .Select(property => property.Value)
                    .OfType<JArray>()
                    .FirstOrDefault() ??
                    obj.Descendants()
                        .OfType<JArray>()
                        .FirstOrDefault(items =>
                            items.Count == 0 ||
                            items[0]?["symbol"] is not null);
        }
        if (array is null)
        {
            throw new InvalidOperationException(
                $"SET returned an unsupported JSON layout for {operation}.");
        }

        try
        {
            return array.ToObject<T[]>(
                JsonSerializer.Create(_jsonSettings)) ?? [];
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"SET returned an invalid {operation} payload.");
        }
    }

    private async Task<byte[]> GetPayload(
        Uri address,
        string operation,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, address);
                request.Headers.TryAddWithoutValidation(
                    "api-key", _apiKey);
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }

                var payload = await ReadLimited(
                    response.Content,
                    operation,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = TryParseApiError(
                        payload, operation);
                    if (error is not null)
                        throw error;

                    throw new HttpRequestException(
                        $"SET {operation} request failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}): " +
                        Sanitize(GetErrorMessage(payload)),
                        null,
                        response.StatusCode);
                }

                return payload;
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"SET {operation} transport request failed after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
            catch (TaskCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= 3)
                {
                    throw new TimeoutException(
                        $"SET {operation} request timed out after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"SET {operation} request exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var queryString = string.Join(
            "&",
            query
                .Where(pair => !pair.Value.IsEmpty())
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(
            new Uri(_address, path.ThrowIfEmpty(nameof(path))))
        {
            Query = queryString,
        }.Uri;
    }

    private SetMarketDataApiException TryParseApiError(
        byte[] payload,
        string operation)
    {
        try
        {
            return GetApiError(
                JToken.Parse(Encoding.UTF8.GetString(payload)),
                operation);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private SetMarketDataApiException GetApiError(
        JToken root,
        string operation)
    {
        if (root is not JObject obj)
            return null;

        var status = obj["status"]?.ToString();
        var message = obj["message"]?.ToString()
            .IsEmpty(obj["detail"]?.ToString());
        var isFailure =
            status.EqualsIgnoreCase("fail") ||
            status.EqualsIgnoreCase("error") ||
            (int.TryParse(
                    status,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var statusCode) &&
                statusCode >= 400);
        if (!isFailure)
            return null;

        return new SetMarketDataApiException(
            status,
            $"SET {operation} request failed " +
            $"({status.IsEmpty("unknown")}): " +
            Sanitize(
                message.IsEmpty("No error description.")));
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > _payloadLimit)
        {
            throw new InvalidOperationException(
                $"SET {operation} response exceeds the 64 MB safety limit.");
        }

        await using var source =
            await content.ReadAsStreamAsync(cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await source.ReadAsync(
                buffer.AsMemory(), cancellationToken);
            if (read == 0)
                break;

            total = checked(total + read);
            if (total > _payloadLimit)
            {
                throw new InvalidOperationException(
                    $"SET {operation} response exceeds the 64 MB safety limit.");
            }

            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
    }

    private string Sanitize(string value)
        => value.IsEmpty()
            ? value
            : value.Replace(
                _apiKey,
                "[redacted]",
                StringComparison.Ordinal);

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";

        var body = Encoding.UTF8.GetString(payload);
        if (body.Length > 2000)
            body = body[..2000];

        try
        {
            var json = JObject.Parse(body);
            return json["message"]?.ToString()
                .IsEmpty(json["detail"]?.ToString())
                .IsEmpty(body);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
    {
        var delay = response.Headers.RetryAfter?.Delta;
        if (delay is null &&
            response.Headers.RetryAfter?.Date is not null)
        {
            delay = response.Headers.RetryAfter.Date.Value -
                DateTimeOffset.UtcNow;
        }

        if (delay is not null && delay.Value > TimeSpan.Zero)
        {
            return delay > TimeSpan.FromSeconds(30)
                ? TimeSpan.FromSeconds(30)
                : delay.Value;
        }

        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "SET API address must be an absolute HTTPS URI.",
                nameof(address));
        }

        var value = address.AbsoluteUri;
        return value.EndsWith('/')
            ? address
            : new Uri(value + "/");
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        base.DisposeManaged();
    }
}
