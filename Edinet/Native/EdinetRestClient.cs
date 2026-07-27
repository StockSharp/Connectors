namespace StockSharp.Edinet.Native;

sealed class EdinetApiException : InvalidOperationException
{
    public EdinetApiException(string status, string message)
        : base(message)
    {
        Status = status;
    }

    public string Status { get; }
}

sealed class EdinetRestClient : BaseLogReceiver, IDisposable
{
    private const int _metadataLimit = 32 * 1024 * 1024;
    private const int _codeListLimit = 64 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly Uri _codeListAddress;
    private readonly string _apiKey;
    private readonly int _documentLimit;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public EdinetRestClient(
        Uri address,
        string apiKey,
        Uri codeListAddress,
        int maxDocumentSizeMb,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureApiAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _codeListAddress = EnsureHttpsAddress(
            codeListAddress ??
            throw new ArgumentNullException(nameof(codeListAddress)),
            nameof(codeListAddress));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
        if (maxDocumentSizeMb is < 1 or > 2047)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDocumentSizeMb));
        }

        _documentLimit = checked(maxDocumentSizeMb * 1024 * 1024);

        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(5);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-Edinet/1.0");
        _delay = delay ?? Task.Delay;
    }

    public async Task<EdinetCompany[]> GetCompanies(
        CancellationToken cancellationToken)
    {
        var response = await GetPayload(
            _codeListAddress,
            "company code list",
            _metadataLimit,
            cancellationToken);
        return ReadCompanies(response.Payload);
    }

    public async Task<EdinetDocument[]> GetDocuments(
        DateTime date,
        CancellationToken cancellationToken)
    {
        var response = await GetPayload(
            BuildApiAddress(
                "documents.json",
                new Dictionary<string, string>
                {
                    ["date"] = date.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    ["type"] = "2",
                }),
            $"document list for {date:yyyy-MM-dd}",
            _metadataLimit,
            cancellationToken);
        var result = DeserializeList(
            response.Payload,
            $"document list for {date:yyyy-MM-dd}",
            _apiKey);
        return result.Results ?? [];
    }

    public async Task<byte[]> DownloadDocument(
        string documentId,
        EdinetDocumentFormats format,
        CancellationToken cancellationToken)
    {
        ValidateDocumentId(documentId);
        ValidateFormat(format);

        var operation =
            $"document '{documentId}' in format {(int)format}";
        var response = await GetPayload(
            BuildApiAddress(
                $"documents/{documentId}",
                new Dictionary<string, string>
                {
                    ["type"] = ((int)format).ToString(
                        CultureInfo.InvariantCulture),
                }),
            operation,
            _documentLimit,
            cancellationToken);

        if (IsJson(response.ContentType, response.Payload))
        {
            throw ParseApiError(
                response.Payload, operation, _apiKey) ??
                new InvalidOperationException(
                    $"EDINET returned JSON instead of a document for {operation}.");
        }

        var isPdf = format == EdinetDocumentFormats.Pdf;
        var valid = isPdf
            ? HasPrefix(response.Payload, "%PDF")
            : response.Payload is { Length: >= 2 } &&
                response.Payload[0] == (byte)'P' &&
                response.Payload[1] == (byte)'K';
        if (!valid)
        {
            throw new InvalidOperationException(
                $"EDINET returned an invalid {(isPdf ? "PDF" : "ZIP")} payload for {operation}.");
        }

        return response.Payload;
    }

    internal static EdinetCompany[] ReadCompanies(byte[] payload)
    {
        if (payload is null ||
            payload.Length < 2 ||
            payload[0] != (byte)'P' ||
            payload[1] != (byte)'K')
        {
            throw new InvalidOperationException(
                "EDINET company code list is not a ZIP archive.");
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var buffer = new MemoryStream(payload, writable: false);
        using var archive = new ZipArchive(
            buffer, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(
                ".csv", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            throw new InvalidOperationException(
                "EDINET company code archive contains no CSV file.");
        }
        if (entry.Length > _codeListLimit)
        {
            throw new InvalidOperationException(
                "EDINET company code CSV exceeds the 64 MB safety limit.");
        }

        string text;
        using (var stream = entry.Open())
        using (var reader = new StreamReader(
            stream,
            Encoding.GetEncoding(932),
            detectEncodingFromByteOrderMarks: true))
        {
            text = reader.ReadToEnd();
        }

        var rows = ParseCsv(text).ToArray();
        var headerIndex = Array.FindIndex(
            rows,
            row => row.Length > 0 &&
                NormalizeHeader(row[0])
                    .EqualsIgnoreCase("EDINET Code"));
        if (headerIndex < 0)
        {
            throw new InvalidOperationException(
                "EDINET company code CSV header was not found.");
        }

        var headers = rows[headerIndex]
            .Select(NormalizeHeader)
            .ToArray();
        var edinet = FindColumn(headers, "EDINET Code");
        var submitterType =
            FindColumn(headers, "Type of Submitter");
        var listing = FindColumn(
            headers, "Listed company / Unlisted company");
        var consolidation = FindColumn(
            headers, "Consolidated / NonConsolidated");
        var capital = FindColumn(headers, "Capital stock");
        var closing = FindColumn(headers, "account closing date");
        var name = FindColumn(headers, "Submitter Name");
        var englishName = FindColumn(
            headers,
            value =>
                value.StartsWith(
                    "Submitter Name",
                    StringComparison.OrdinalIgnoreCase) &&
                value.Contains(
                    "alphabetic",
                    StringComparison.OrdinalIgnoreCase));
        var phoneticName = FindColumn(
            headers,
            value =>
                value.StartsWith(
                    "Submitter Name",
                    StringComparison.OrdinalIgnoreCase) &&
                value.Contains(
                    "phonetic",
                    StringComparison.OrdinalIgnoreCase));
        var province = FindColumn(headers, "Province");
        var industry =
            FindColumn(headers, "Submitter's industry");
        var securities = FindColumn(
            headers, "Securities Identification Code");
        var corporate = FindColumn(
            headers, "Submitter's Japan Corporate Number");

        return rows
            .Skip(headerIndex + 1)
            .Where(row => !Get(row, edinet).IsEmpty())
            .Select(row => new EdinetCompany
            {
                EdinetCode = Get(row, edinet),
                SubmitterType = Get(row, submitterType),
                ListingStatus = Get(row, listing),
                ConsolidationStatus = Get(row, consolidation),
                CapitalStock = Get(row, capital),
                ClosingDate = Get(row, closing),
                Name = Get(row, name),
                EnglishName = Get(row, englishName),
                PhoneticName = Get(row, phoneticName),
                Province = Get(row, province),
                Industry = Get(row, industry),
                SecuritiesCode = Get(row, securities),
                CorporateNumber = Get(row, corporate),
            })
            .ToArray();
    }

    private async Task<EdinetPayload> GetPayload(
        Uri address,
        string operation,
        int maxBytes,
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
                    maxBytes,
                    operation,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var apiError = ParseApiError(
                        payload, operation, _apiKey);
                    if (apiError is not null)
                        throw apiError;

                    throw new HttpRequestException(
                        $"EDINET {operation} request failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}): " +
                        Sanitize(
                            GetErrorMessage(payload), _apiKey),
                        null,
                        response.StatusCode);
                }

                return new(
                    payload,
                    response.Content.Headers.ContentType?
                        .MediaType);
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"EDINET {operation} transport request failed after four attempts.");
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
                        $"EDINET {operation} request timed out after four attempts.");
                }

                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"EDINET {operation} request exhausted its retry limit.");
    }

    private Uri BuildApiAddress(
        string path,
        IReadOnlyDictionary<string, string> parameters)
    {
        var pairs = parameters
            .Where(pair => !pair.Value.IsEmpty())
            .Concat(
            [
                new KeyValuePair<string, string>(
                    "Subscription-Key", _apiKey),
            ]);
        var query = string.Join(
            "&",
            pairs.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(
            new Uri(_address, path.ThrowIfEmpty(nameof(path))))
        {
            Query = query,
        }.Uri;
    }

    private static EdinetListResponse DeserializeList(
        byte[] payload,
        string operation,
        string apiKey)
    {
        EdinetListResponse result;
        try
        {
            result = JsonConvert.DeserializeObject<EdinetListResponse>(
                Encoding.UTF8.GetString(payload ?? []),
                _jsonSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"EDINET returned invalid JSON for {operation}.", ex);
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                $"EDINET returned an empty response for {operation}.");
        }
        if (result.StatusCode is int errorStatus &&
            errorStatus != 200)
        {
            throw CreateApiError(
                errorStatus.ToString(CultureInfo.InvariantCulture),
                result.ErrorMessage,
                operation,
                apiKey);
        }

        var status = result.Metadata?.Status;
        if (status != "200")
        {
            throw CreateApiError(
                status,
                result.Metadata?.Message,
                operation,
                apiKey);
        }

        return result;
    }

    private static EdinetApiException ParseApiError(
        byte[] payload,
        string operation,
        string apiKey)
    {
        if (payload is null || payload.Length == 0)
            return null;

        try
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(payload));
            var status = json["StatusCode"]?.ToString()
                .IsEmpty(json["status"]?.ToString())
                .IsEmpty(
                    json["metadata"]?["status"]?.ToString());
            var message = json["message"]?.ToString()
                .IsEmpty(
                    json["metadata"]?["message"]?.ToString());

            return status.IsEmpty() && message.IsEmpty()
                ? null
                : CreateApiError(
                    status, message, operation, apiKey);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static EdinetApiException CreateApiError(
        string status,
        string message,
        string operation,
        string apiKey)
        => new(
            status,
            $"EDINET {operation} request failed " +
            $"({status.IsEmpty("unknown")}): " +
            Sanitize(
                message.IsEmpty("No error description."),
                apiKey));

    private static string Sanitize(
        string value,
        string apiKey)
        => value.IsEmpty() || apiKey.IsEmpty()
            ? value
            : value.Replace(
                apiKey,
                "[redacted]",
                StringComparison.Ordinal);

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        int maxBytes,
        string operation,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > maxBytes)
        {
            throw new InvalidOperationException(
                $"EDINET {operation} response exceeds the configured " +
                $"{maxBytes / (1024 * 1024)} MB limit.");
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
            if (total > maxBytes)
            {
                throw new InvalidOperationException(
                    $"EDINET {operation} response exceeds the configured " +
                    $"{maxBytes / (1024 * 1024)} MB limit.");
            }

            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }

        return target.ToArray();
    }

    private static IEnumerable<string[]> ParseCsv(string text)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var index = 0; index < text.Length; index++)
        {
            var value = text[index];
            if (quoted)
            {
                if (value == '"' &&
                    index + 1 < text.Length &&
                    text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (value == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(value);
                }

                continue;
            }

            if (value == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (value == ',')
            {
                row.Add(field.ToString().Trim());
                field.Clear();
            }
            else if (value is '\r' or '\n')
            {
                if (value == '\r' &&
                    index + 1 < text.Length &&
                    text[index + 1] == '\n')
                {
                    index++;
                }

                row.Add(field.ToString().Trim());
                field.Clear();
                if (row.Any(value => !value.IsEmpty()))
                    yield return row.ToArray();
                row.Clear();
            }
            else
            {
                field.Append(value);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().Trim());
            if (row.Any(value => !value.IsEmpty()))
                yield return row.ToArray();
        }
    }

    private static int FindColumn(
        string[] headers,
        string name)
        => FindColumn(
            headers,
            value => value.EqualsIgnoreCase(name));

    private static int FindColumn(
        string[] headers,
        Func<string, bool> predicate)
    {
        var index = Array.FindIndex(
            headers, value => predicate(value));
        if (index < 0)
        {
            throw new InvalidOperationException(
                "EDINET company code CSV has an unsupported column layout.");
        }

        return index;
    }

    private static string NormalizeHeader(string value)
        => value?
            .Trim()
            .TrimStart('\uFEFF')
            .Replace("\uFF08", "(", StringComparison.Ordinal)
            .Replace("\uFF09", ")", StringComparison.Ordinal);

    private static string Get(string[] values, int index)
        => index >= 0 && index < values.Length
            ? values[index]?.Trim()
            : null;

    private static void ValidateDocumentId(string documentId)
    {
        if (documentId.IsEmpty() ||
            documentId.Length > 32 ||
            !documentId.All(value =>
                char.IsAsciiLetterOrDigit(value) ||
                value is '-' or '_'))
        {
            throw new ArgumentException(
                "EDINET document ID must contain only ASCII letters, digits, hyphens, or underscores.",
                nameof(documentId));
        }
    }

    private static void ValidateFormat(EdinetDocumentFormats format)
    {
        if ((int)format is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(format), format, null);
        }
    }

    private static bool IsJson(
        string contentType,
        byte[] payload)
    {
        if (contentType?.Contains(
            "json", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return payload?
            .SkipWhile(value => char.IsWhiteSpace((char)value))
            .FirstOrDefault() == (byte)'{';
    }

    private static bool HasPrefix(byte[] payload, string value)
        => payload is not null &&
            payload.Length >= value.Length &&
            Encoding.ASCII.GetString(
                payload, 0, value.Length) == value;

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
            return json["message"]?.ToString().IsEmpty(body);
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

    private static Uri EnsureApiAddress(Uri address)
    {
        address = EnsureHttpsAddress(address, nameof(address));
        var value = address.AbsoluteUri;
        return value.EndsWith('/')
            ? address
            : new Uri(value + "/");
    }

    private static Uri EnsureHttpsAddress(
        Uri address,
        string parameterName)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "EDINET addresses must be absolute HTTPS URIs.",
                parameterName);
        }

        return address;
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        base.DisposeManaged();
    }

    private readonly record struct EdinetPayload(
        byte[] Payload,
        string ContentType);
}
