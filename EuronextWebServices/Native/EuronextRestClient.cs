namespace StockSharp.EuronextWebServices.Native;

sealed class EuronextRestClient :
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
    private readonly string _authKey;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EuronextRestClient(
        Uri address,
        string authKey,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _authKey = authKey.ThrowIfEmpty(nameof(authKey));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-EuronextWebServices/1.0");
        _delay = delay ?? Task.Delay;
    }

    public async Task<EuronextInstrument> GetInstrument(
        string isin,
        string mic,
        EuronextSessionQualities sessionQuality,
        CancellationToken cancellationToken)
    {
        ValidateInstrument(isin, mic);
        var response = await GetJson<EuronextInstrumentResponse>(
            "instrumentDetail",
            new Dictionary<string, string>
            {
                ["code"] = isin.ToUpperInvariant(),
                ["exchCode"] = mic.ToUpperInvariant(),
                ["view"] = "FULL",
                ["sessionQuality"] = sessionQuality.ToApiCode(),
                ["authKey"] = _authKey,
            },
            "instrument detail",
            cancellationToken);
        ValidateStatus(response.Status, "instrument detail");
        return response.Instrument ??
            throw new InvalidOperationException(
                "Euronext returned no instrument object.");
    }

    public async Task<EuronextIntradayResponse> GetIntraday(
        string isin,
        string mic,
        EuronextSessionQualities sessionQuality,
        bool trades,
        TimeSpan timeFrame,
        int depth,
        CancellationToken cancellationToken)
    {
        ValidateInstrument(isin, mic);
        if (depth is < 1 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(depth), depth,
                "Euronext intraday depth must be one or two sessions.");
        }
        if (!trades &&
            (timeFrame < TimeSpan.FromSeconds(1) ||
                timeFrame > TimeSpan.FromDays(1) ||
                timeFrame.TotalMilliseconds % 1 != 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame,
                "Euronext bar resolution must be from one second to one day in whole milliseconds.");
        }

        var query = new Dictionary<string, string>
        {
            ["code"] = isin.ToUpperInvariant(),
            ["codification"] = "ISIN",
            ["exchCode"] = mic.ToUpperInvariant(),
            ["view"] = "FULL",
            ["depth"] = depth.ToString(
                CultureInfo.InvariantCulture),
            ["type"] = trades ? "TRA" : "MIN",
            ["sessionQuality"] = sessionQuality.ToApiCode(),
            ["authKey"] = _authKey,
        };
        if (!trades)
        {
            query["resolution"] = checked(
                (long)timeFrame.TotalMilliseconds)
                .ToString(CultureInfo.InvariantCulture);
        }

        var response = await GetJson<EuronextIntradayResponse>(
            "intraday",
            query,
            trades ? "intraday trades" : "intraday bars",
            cancellationToken);
        ValidateStatus(
            response.Status,
            trades ? "intraday trades" : "intraday bars");
        response.Points ??= [];
        return response;
    }

    private async Task<T> GetJson<T>(
        string path,
        IReadOnlyDictionary<string, string> query,
        string operation,
        CancellationToken cancellationToken)
    {
        var address = BuildAddress(path, query);
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, address);
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var payload = await ReadLimited(
                    response.Content,
                    operation,
                    cancellationToken);

                if (IsTransient(response.StatusCode) &&
                    attempt < 3)
                {
                    await _delay(
                        GetRetryDelay(response, attempt),
                        cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    throw new EuronextWebServicesApiException(
                        response.StatusCode,
                        null,
                        $"Euronext {operation} request failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(GetErrorMessage(payload)));
                }

                return Deserialize<T>(payload, operation);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"Euronext {operation} transport request " +
                        "failed after four attempts.");
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
                        $"Euronext {operation} request timed out " +
                        "after four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"Euronext {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(_address, path);
        var queryString = string.Join(
            "&",
            query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(resource)
        {
            Query = queryString,
        }.Uri;
    }

    private static T Deserialize<T>(
        byte[] payload,
        string operation)
    {
        try
        {
            var value = JsonConvert.DeserializeObject<T>(
                Encoding.UTF8.GetString(payload),
                _jsonSettings);
            return value is null
                ? throw new InvalidOperationException(
                    $"Euronext returned an empty {operation} payload.")
                : value;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Euronext returned invalid JSON for {operation}.");
        }
    }

    private static void ValidateInstrument(
        string isin,
        string mic)
        => new SecurityId
        {
            SecurityCode = isin,
            BoardCode = mic,
        }.GetEuronextId();

    private static void ValidateStatus(
        string status,
        string operation)
    {
        if (status.IsEmpty() ||
            status == "0" ||
            status.EqualsIgnoreCase("OK"))
        {
            return;
        }

        throw new EuronextWebServicesApiException(
            null,
            status,
            $"Euronext {operation} returned API status " +
            $"'{status}'.");
    }

    private async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > _payloadLimit)
        {
            throw new InvalidOperationException(
                $"Euronext {operation} response exceeds " +
                "the 64 MB safety limit.");
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
                    $"Euronext {operation} response exceeds " +
                    "the 64 MB safety limit.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private string Sanitize(string value)
    {
        if (value.IsEmpty())
            return "empty response";
        value = value.Replace(
            _authKey,
            "[redacted]",
            StringComparison.Ordinal);
        return new string(value
            .Take(2000)
            .Select(character =>
                char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t'
                    ? ' '
                    : character)
            .ToArray())
            .Trim();
    }

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";

        var body = Encoding.UTF8.GetString(payload);
        try
        {
            var root = JObject.Parse(body);
            return root["message"]?.ToString()
                .IsEmpty(root["error"]?.ToString())
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
                "Euronext address must be an absolute HTTPS URI.",
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
