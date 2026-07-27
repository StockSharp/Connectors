namespace StockSharp.EsmaFirds.Native;

sealed class EsmaFirdsRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _payloadLimit = 64 * 1024 * 1024;

    private const string _fields =
        "id,isin,mic,gnr_full_name,gnr_short_name,gnr_cfi_code," +
        "gnr_notional_curr_code,lei,mrkt_issr_trdng_rqst_flag," +
        "mrkt_trdng_start_date,mrkt_trdng_trmination_date,rca_mic," +
        "status,status_label,publication_date,latest_received_flag," +
        "never_published_flag";

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EsmaFirdsRestClient(
        Uri address,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-EsmaFirds/1.0");
        _delay = delay ?? Task.Delay;
    }

    public async Task<EsmaSolrResponse<EsmaInstrument>>
        SearchInstruments(
            EsmaInstrumentSearch search,
            CancellationToken cancellationToken)
    {
        if (search is null)
            throw new ArgumentNullException(nameof(search));
        if (search.Start < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(search), search.Start,
                "ESMA FIRDS result offset cannot be negative.");
        }
        if (search.Rows is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(search), search.Rows,
                "ESMA FIRDS result count must be from 1 to 1000.");
        }

        var filters = new List<string>
        {
            "latest_received_flag:1",
            "never_published_flag:0",
        };

        var categories = (search.CfiCategories ?? [])
            .Where(category => !category.IsEmpty())
            .Select(category => category.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (categories.Length > 0)
        {
            if (categories.Any(category =>
                category.Length != 1 ||
                !char.IsAsciiLetterUpper(category[0])))
            {
                throw new ArgumentException(
                    "Each ESMA FIRDS CFI category must be one uppercase letter.",
                    nameof(search));
            }

            filters.Add(
                "(" +
                string.Join(
                    " OR ",
                    categories.Select(category =>
                        $"gnr_cfi_code:{category}*")) +
                ")");
        }

        if (!search.Mic.IsEmpty())
        {
            var mic = search.Mic.Trim().ToUpperInvariant();
            if (mic.Length != 4 ||
                mic.Any(character =>
                    !char.IsAsciiLetterOrDigit(character)))
            {
                throw new ArgumentException(
                    "ESMA FIRDS MIC must contain four letters or digits.",
                    nameof(search));
            }

            filters.Add($"mic:\"{mic.EscapeSolr()}\"");
        }

        if (search.ActiveOnly)
        {
            filters.Add(
                "((*:* -mrkt_trdng_trmination_date:[* TO *]) OR " +
                "mrkt_trdng_trmination_date:[NOW TO *])");
        }

        var address = BuildAddress(
            "solr/esma_registers_firds/select",
            new Dictionary<string, string>
            {
                ["q"] = search.Value.ToSolrQuery(),
                ["fq"] = string.Join(" AND ", filters),
                ["fl"] = _fields,
                ["sort"] = "publication_date desc",
                ["start"] = search.Start.ToString(
                    CultureInfo.InvariantCulture),
                ["rows"] = search.Rows.ToString(
                    CultureInfo.InvariantCulture),
                ["wt"] = "json",
            });

        var envelope = await GetJson<
            EsmaSolrEnvelope<EsmaInstrument>>(
                address,
                "instrument lookup",
                cancellationToken);
        if (envelope.Error is not null)
        {
            throw new EsmaFirdsApiException(
                null,
                envelope.Error.Code,
                $"ESMA FIRDS instrument lookup failed" +
                FormatSolrCode(envelope.Error.Code) +
                $": {Sanitize(envelope.Error.Message)}");
        }
        if (envelope.Header is not null &&
            envelope.Header.Status != 0)
        {
            throw new EsmaFirdsApiException(
                null,
                envelope.Header.Status,
                "ESMA FIRDS instrument lookup returned " +
                $"Solr status {envelope.Header.Status}.");
        }
        if (envelope.Response is null)
        {
            throw new InvalidOperationException(
                "ESMA FIRDS returned no Solr response object.");
        }

        envelope.Response.Documents ??= [];
        return envelope.Response;
    }

    private async Task<T> GetJson<T>(
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
                    throw CreateApiException(
                        response.StatusCode,
                        payload,
                        operation);
                }

                return Deserialize<T>(payload, operation);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"ESMA FIRDS {operation} transport request " +
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
                        $"ESMA FIRDS {operation} request timed out " +
                        "after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"ESMA FIRDS {operation} exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
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
                    $"ESMA FIRDS returned an empty {operation} payload.")
                : value;
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"ESMA FIRDS returned invalid JSON for {operation}.");
        }
    }

    private static EsmaFirdsApiException CreateApiException(
        HttpStatusCode statusCode,
        byte[] payload,
        string operation)
    {
        int? solrCode = null;
        string message = null;
        try
        {
            var envelope =
                JsonConvert.DeserializeObject<
                    EsmaSolrEnvelope<EsmaInstrument>>(
                        Encoding.UTF8.GetString(payload),
                        _jsonSettings);
            solrCode = envelope?.Error?.Code;
            message = envelope?.Error?.Message;
        }
        catch (JsonException)
        {
        }

        message = Sanitize(message.IsEmpty()
            ? GetErrorMessage(payload)
            : message);
        return new EsmaFirdsApiException(
            statusCode,
            solrCode,
            $"ESMA FIRDS {operation} request failed " +
            $"({(int)statusCode} {statusCode}" +
            FormatSolrCode(solrCode) +
            $"): {message}");
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
                $"ESMA FIRDS {operation} response exceeds " +
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
                    $"ESMA FIRDS {operation} response exceeds " +
                    "the 64 MB safety limit.");
            }

            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
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

    private static string GetErrorMessage(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
            return "empty response";

        var body = Encoding.UTF8.GetString(payload);
        if (body.Length > 2000)
            body = body[..2000];
        return body;
    }

    private static string Sanitize(string value)
    {
        if (value.IsEmpty())
            return "unknown error";

        var filtered = new string(value
            .Take(2000)
            .Select(character =>
                char.IsControl(character) &&
                character is not '\r' and not '\n' and not '\t'
                    ? ' '
                    : character)
            .ToArray());
        return filtered.Trim();
    }

    private static string FormatSolrCode(int? code)
        => code is null ? string.Empty : $", Solr {code}";

    private static Uri EnsureAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "ESMA FIRDS address must be an absolute HTTPS URI.",
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
