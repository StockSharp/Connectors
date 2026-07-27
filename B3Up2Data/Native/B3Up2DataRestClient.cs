namespace StockSharp.B3Up2Data.Native;

sealed class B3Up2DataRestClient :
    BaseLogReceiver,
    IDisposable
{
    private const int _metadataLimit = 32 * 1024 * 1024;
    private const int _fileLimit = 128 * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly HashSet<string> _secrets = new(
        StringComparer.Ordinal);

    private Uri _sasUri;

    public B3Up2DataRestClient(
        Uri address,
        HttpMessageHandler handler = null,
        Func<TimeSpan, CancellationToken, Task> delay = null)
    {
        _address = EnsureApiAddress(
            address ?? throw new ArgumentNullException(nameof(address)));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(5);
        _delay = delay ?? Task.Delay;
    }

    public async Task<B3AccessToken> GetAccessToken(
        string certificate,
        string password,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        certificate = ValidateCertificate(certificate);
        password = ValidateCredential(password, "certificate password");
        clientId = ValidateCredential(clientId, "client ID");
        clientSecret = ValidateCredential(clientSecret, "client secret");
        RememberSecret(certificate);
        RememberSecret(password);
        RememberSecret(clientId);
        RememberSecret(clientSecret);

        var result = await Send(
            () =>
            {
                var request = CreateApiPost(
                    "security/oauth/token");
                request.Headers.TryAddWithoutValidation(
                    "certificate", certificate);
                request.Headers.TryAddWithoutValidation(
                    "password", password);
                request.Headers.TryAddWithoutValidation(
                    "clientId", clientId);
                request.Headers.TryAddWithoutValidation(
                    "clientSecret", clientSecret);
                return request;
            },
            "access-token",
            _metadataLimit,
            cancellationToken);
        var token = Deserialize<B3AccessToken>(
            ParseJson(result.Content, "access-token"),
            "access-token");
        token.AccessToken = ValidateToken(token.AccessToken);
        RememberSecret(token.AccessToken);
        return token;
    }

    public async Task<B3SasChannel[]> GenerateSas(
        string accessToken,
        string certificate,
        string password,
        CancellationToken cancellationToken)
    {
        accessToken = ValidateToken(accessToken);
        certificate = ValidateCertificate(certificate);
        password = ValidateCredential(password, "certificate password");
        RememberSecret(accessToken);
        RememberSecret(certificate);
        RememberSecret(password);

        var result = await Send(
            () =>
            {
                var request = CreateApiPost(
                    "security/storage/sas");
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer", accessToken);
                request.Headers.TryAddWithoutValidation(
                    "certificate", certificate);
                request.Headers.TryAddWithoutValidation(
                    "password", password);
                return request;
            },
            "SAS generation",
            _metadataLimit,
            cancellationToken);
        var document = ParseJson(
            result.Content, "SAS generation");
        var array = document as JArray;
        if (array is null && document is JObject obj)
        {
            array = obj["data"] as JArray ??
                obj["channels"] as JArray ??
                obj["sas"] as JArray;
        }
        if (array is null)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA returned an invalid SAS-channel schema.");
        }
        var channels = Deserialize<B3SasChannel[]>(
            array, "SAS channels")
            .Where(channel =>
                channel is not null &&
                !channel.Name.IsEmpty() &&
                !channel.Sas.IsEmpty())
            .ToArray();
        if (channels.Length == 0)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA returned no contracted SAS channels.");
        }
        return channels;
    }

    public void SetSas(string sas)
    {
        var uri = EnsureSasUri(sas);
        _sasUri = uri;
        RememberSecret(uri.Query);
        foreach (var value in ParseQuery(uri.Query).Values)
            RememberSecret(value);
    }

    public async Task<B3BlobPage> ListBlobs(
        string prefix,
        string marker,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (maxResults is < 1 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(maxResults));
        prefix = B3Up2DataExtensions.NormalizeBlobPrefix(prefix);
        if (marker?.Length > 4096 ||
            marker?.Any(char.IsControl) == true)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA continuation marker is invalid.");
        }

        var result = await Send(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildListAddress(prefix, marker, maxResults));
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/xml"));
                AddUserAgent(request);
                return request;
            },
            "blob listing",
            _metadataLimit,
            cancellationToken);
        return ParseBlobPage(result.Content);
    }

    public async Task<B3DownloadedBlob> DownloadBlob(
        string name,
        CancellationToken cancellationToken)
    {
        name = B3Up2DataExtensions.ValidateBlobName(name);
        var result = await Send(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    BuildBlobAddress(name));
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("*/*"));
                AddUserAgent(request);
                return request;
            },
            $"blob download '{Path.GetFileName(name)}'",
            _fileLimit,
            cancellationToken);
        return new B3DownloadedBlob
        {
            Name = name,
            Content = result.Content,
            ContentType = result.ContentType,
            LastModified = result.LastModified,
            ETag = result.ETag,
        };
    }

    private HttpRequestMessage CreateApiPost(string path)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(_address, path))
        {
            Content = new ByteArrayContent([]),
        };
        request.Content.Headers.ContentType =
            new MediaTypeHeaderValue("application/json");
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        AddUserAgent(request);
        return request;
    }

    private async Task<B3HttpResult> Send(
        Func<HttpRequestMessage> requestFactory,
        string operation,
        int payloadLimit,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var request = requestFactory();
                using var response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                var content = await ReadLimited(
                    response.Content,
                    operation,
                    payloadLimit,
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
                    var requestId =
                        GetHeader(response, "x-ms-request-id")
                            .IsEmpty(GetHeader(
                                response, "X-Request-Id"))
                            .IsEmpty(GetHeader(
                                response, "x-amzn-RequestId"));
                    throw new B3Up2DataApiException(
                        response.StatusCode,
                        requestId,
                        $"B3 UP2DATA {operation} failed " +
                        $"({(int)response.StatusCode} " +
                        $"{response.StatusCode}): " +
                        Sanitize(GetError(content)));
                }

                return new B3HttpResult(
                    content,
                    response.Content.Headers.ContentType?.ToString(),
                    response.Content.Headers.LastModified?.UtcDateTime,
                    response.Headers.ETag?.Tag,
                    GetHeader(response, "x-ms-request-id"));
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null)
            {
                if (attempt >= 3)
                {
                    throw new HttpRequestException(
                        $"B3 UP2DATA {operation} transport request " +
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
                        $"B3 UP2DATA {operation} timed out after " +
                        "four attempts.");
                }
                await _delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"B3 UP2DATA {operation} exhausted its retry limit.");
    }

    private Uri BuildListAddress(
        string prefix,
        string marker,
        int maxResults)
    {
        var sas = SafeSas();
        var query = new List<string>
        {
            "restype=container",
            "comp=list",
            "maxresults=" +
                maxResults.ToString(CultureInfo.InvariantCulture),
        };
        if (!prefix.IsEmpty())
        {
            query.Add(
                "prefix=" + Uri.EscapeDataString(prefix));
        }
        if (!marker.IsEmpty())
        {
            query.Add(
                "marker=" + Uri.EscapeDataString(marker));
        }
        if (!sas.Query.IsEmpty())
            query.Add(sas.Query.TrimStart('?'));
        return new UriBuilder(sas)
        {
            Query = string.Join("&", query),
        }.Uri;
    }

    private Uri BuildBlobAddress(string name)
    {
        var sas = SafeSas();
        var encodedName = string.Join(
            "/",
            name.Split('/').Select(Uri.EscapeDataString));
        var root = sas.GetLeftPart(UriPartial.Authority) +
            sas.AbsolutePath.TrimEnd('/');
        return new Uri(
            $"{root}/{encodedName}{sas.Query}",
            UriKind.Absolute);
    }

    private Uri SafeSas()
        => _sasUri ?? throw new InvalidOperationException(
            "B3 UP2DATA SAS URI is not initialized.");

    private static B3BlobPage ParseBlobPage(byte[] content)
    {
        try
        {
            using var stream = new MemoryStream(content, false);
            using var reader = XmlReader.Create(
                stream,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var document = XDocument.Load(
                reader, LoadOptions.None);
            var blobs = document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "Blob")
                .Select(element =>
                {
                    var properties = element.Elements()
                        .FirstOrDefault(child =>
                            child.Name.LocalName == "Properties");
                    return new B3BlobItem
                    {
                        Name = LocalValue(element, "Name"),
                        LastModified = ParseHttpDate(
                            LocalValue(properties, "Last-Modified")),
                        ContentLength = ParseLong(
                            LocalValue(properties, "Content-Length")),
                        ContentType = LocalValue(
                            properties, "Content-Type"),
                        ETag = LocalValue(properties, "Etag")
                            .IsEmpty(LocalValue(properties, "ETag")),
                    };
                })
                .Where(blob => !blob.Name.IsEmpty())
                .ToArray();
            var nextMarker = document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "NextMarker")
                ?.Value
                ?.Trim();
            return new B3BlobPage
            {
                Items = blobs,
                NextMarker = nextMarker,
            };
        }
        catch (XmlException)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA returned an invalid Azure blob-list schema.");
        }
    }

    private static JToken ParseJson(
        byte[] content,
        string operation)
    {
        try
        {
            var text = Encoding.UTF8.GetString(content);
            if (text.IsEmpty())
            {
                throw new InvalidOperationException(
                    $"B3 UP2DATA returned an empty {operation} response.");
            }
            return JToken.Parse(text);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA returned invalid JSON for {operation}.");
        }
    }

    private static T Deserialize<T>(
        JToken token,
        string operation)
    {
        try
        {
            return token.ToObject<T>(
                JsonSerializer.Create(_jsonSettings)) ??
                throw new InvalidOperationException(
                    $"B3 UP2DATA returned an empty {operation} payload.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA returned an invalid {operation} schema.");
        }
    }

    private static string GetError(byte[] content)
    {
        if (content is null || content.Length == 0)
            return "empty error response";
        var text = Encoding.UTF8.GetString(
            content.Take(8192).ToArray());
        try
        {
            var token = JToken.Parse(text);
            if (token is JObject obj)
            {
                return GetString(obj["message"])
                    .IsEmpty(GetString(obj["error_description"]))
                    .IsEmpty(GetString(obj["error"]))
                    .IsEmpty(GetString(obj["code"]))
                    .IsEmpty(text);
            }
        }
        catch (JsonException)
        {
        }

        try
        {
            using var stringReader = new StringReader(text);
            using var xmlReader = XmlReader.Create(
                stringReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
            var document = XDocument.Load(xmlReader);
            return document
                .Descendants()
                .FirstOrDefault(element =>
                    element.Name.LocalName == "Message")
                ?.Value
                .IsEmpty(document
                    .Descendants()
                    .FirstOrDefault(element =>
                        element.Name.LocalName == "Code")
                    ?.Value)
                .IsEmpty(text);
        }
        catch (XmlException)
        {
            return text;
        }
    }

    private static async Task<byte[]> ReadLimited(
        HttpContent content,
        string operation,
        int limit,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length &&
            length > limit)
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA {operation} response exceeds " +
                $"{limit / (1024 * 1024)} MB.");
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
            if (total > limit)
            {
                throw new InvalidOperationException(
                    $"B3 UP2DATA {operation} response exceeds " +
                    $"{limit / (1024 * 1024)} MB.");
            }
            await target.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken);
        }
        return target.ToArray();
    }

    private static string ValidateCertificate(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            throw new ArgumentNullException(nameof(value));
        if (value.Length > 16 * 1024 * 1024 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA certificate data is invalid.");
        }
        try
        {
            Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "B3 UP2DATA certificate must be base64 encoded.");
        }
        return value;
    }

    private static string ValidateCredential(
        string value,
        string name)
    {
        value = value?.Trim();
        if (value.IsEmpty())
            throw new InvalidOperationException(
                $"B3 UP2DATA {name} is not specified.");
        if (value.Length > 4096 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                $"B3 UP2DATA {name} is invalid.");
        }
        return value;
    }

    private static string ValidateToken(string value)
    {
        value = value?.Trim();
        if (value.IsEmpty() ||
            value.Length > 32768 ||
            value.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA access token is invalid.");
        }
        return value;
    }

    private static Uri EnsureApiAddress(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "B3 UP2DATA API address must be an absolute HTTPS URI.",
                nameof(address));
        }
        return address.AbsoluteUri.EndsWith('/')
            ? address
            : new Uri(address.AbsoluteUri + "/");
    }

    private static Uri EnsureSasUri(string value)
    {
        if (!Uri.TryCreate(
                value?.Trim(),
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.Host.EndsWith(
                ".blob.core.windows.net",
                StringComparison.OrdinalIgnoreCase) ||
            uri.AbsolutePath.Trim('/').IsEmpty() ||
            uri.Query.IsEmpty() ||
            !ParseQuery(uri.Query).ContainsKey("sig"))
        {
            throw new InvalidOperationException(
                "B3 UP2DATA SAS URI must be an HTTPS Azure Blob " +
                "container URI with a signature.");
        }
        return uri;
    }

    private static Dictionary<string, string> ParseQuery(
        string query)
        => query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2))
            .ToDictionary(
                item => Uri.UnescapeDataString(item[0]),
                item => item.Length > 1
                    ? Uri.UnescapeDataString(item[1])
                    : string.Empty,
                StringComparer.OrdinalIgnoreCase);

    private static string LocalValue(
        XElement parent,
        string name)
        => parent?.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == name)
            ?.Value
            ?.Trim();

    private static DateTime? ParseHttpDate(string value)
        => DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces |
            DateTimeStyles.AssumeUniversal |
            DateTimeStyles.AdjustToUniversal,
            out var result)
                ? result.UtcDateTime
                : null;

    private static long? ParseLong(string value)
        => long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;

    private static string GetString(JToken token)
        => token switch
        {
            null => null,
            JValue value => value.ToString(
                CultureInfo.InvariantCulture),
            _ => token.ToString(Newtonsoft.Json.Formatting.None),
        };

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.TooManyRequests ||
            statusCode == HttpStatusCode.BadGateway ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            statusCode == HttpStatusCode.GatewayTimeout ||
            (int)statusCode is >= 500 and <= 511;

    private static TimeSpan GetRetryDelay(
        HttpResponseMessage response,
        int attempt)
        => response.Headers.RetryAfter?.Delta is { } delay &&
            delay > TimeSpan.Zero
                ? delay.Min(TimeSpan.FromSeconds(30))
                : TimeSpan.FromSeconds(Math.Pow(2, attempt));

    private static string GetHeader(
        HttpResponseMessage response,
        string name)
        => response.Headers.TryGetValues(
            name, out var values)
                ? values.FirstOrDefault()
                : null;

    private static void AddUserAgent(
        HttpRequestMessage request)
        => request.Headers.TryAddWithoutValidation(
            "User-Agent", "StockSharp-B3-UP2DATA/1.0");

    private void RememberSecret(string value)
    {
        if (value.IsEmpty())
            return;
        if (value.Length <= 4096)
        {
            _secrets.Add(value);
            return;
        }
        _secrets.Add(value[..Math.Min(128, value.Length)]);
        _secrets.Add(value[(value.Length - Math.Min(128, value.Length))..]);
    }

    private string Sanitize(string value)
    {
        if (value.IsEmpty())
            return "unknown error";
        foreach (var secret in _secrets
            .Where(secret => !secret.IsEmpty())
            .OrderByDescending(secret => secret.Length))
        {
            value = value.Replace(
                secret,
                "[REDACTED]",
                StringComparison.Ordinal);
        }
        return new string(value
            .Take(2000)
            .Select(character =>
                char.IsControl(character) ? ' ' : character)
            .ToArray())
            .Trim();
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        base.DisposeManaged();
    }

    private readonly record struct B3HttpResult(
        byte[] Content,
        string ContentType,
        DateTime? LastModified,
        string ETag,
        string RequestId);
}
