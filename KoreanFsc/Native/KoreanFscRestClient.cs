namespace StockSharp.KoreanFsc.Native;

sealed class KoreanFscApiException : InvalidOperationException
{
    public KoreanFscApiException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

sealed class KoreanFscRestClient : BaseLogReceiver, IDisposable
{
    private static readonly JsonSerializer _serializer =
        JsonSerializer.Create(
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            });

    private readonly Uri _address;
    private readonly string _serviceKey;
    private readonly HttpClient _http;

    public KoreanFscRestClient(
        Uri address,
        string serviceKey,
        HttpMessageHandler handler = null)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _serviceKey = serviceKey.ThrowIfEmpty(nameof(serviceKey));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-KoreanFsc/1.0");
    }

    public async Task<KoreanFscPage> GetPage(
        KoreanFscDataSets dataSet,
        int pageNumber,
        int pageSize,
        KoreanFscQuery query,
        CancellationToken cancellationToken)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber), pageNumber, null);
        }
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), pageSize, null);
        }

        var parameters = new Dictionary<string, string>
        {
            ["numOfRows"] = pageSize.ToString(
                CultureInfo.InvariantCulture),
            ["pageNo"] = pageNumber.ToString(
                CultureInfo.InvariantCulture),
            ["resultType"] = "json",
        };

        parameters.TryAdd(
            "basDt",
            query.ExactDate?.ToString(
                "yyyyMMdd", CultureInfo.InvariantCulture));
        parameters.TryAdd(
            "beginBasDt",
            query.From?.ToString(
                "yyyyMMdd", CultureInfo.InvariantCulture));
        parameters.TryAdd(
            "endBasDt",
            query.ToExclusive?.ToString(
                "yyyyMMdd", CultureInfo.InvariantCulture));
        parameters.TryAdd("likeSrtnCd", query.Symbol);
        parameters.TryAdd("likeItmsNm", query.Name);
        parameters.TryAdd("isinCd", query.Isin);

        if (!query.Market.IsEmpty() &&
            dataSet.SupportsMarketFilter())
        {
            parameters[
                dataSet == KoreanFscDataSets.Stocks
                    ? "mrktCls"
                    : "mrktCtg"] = query.Market;
        }

        var path = dataSet.ToEndpoint();
        var payload = await GetPayload(
            path, parameters, cancellationToken);
        return ParsePage(payload, path);
    }

    private KoreanFscPage ParsePage(
        byte[] payload,
        string path)
    {
        var body = Encoding.UTF8.GetString(payload ?? []);
        if (body.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Korean FSC returned an empty response for '{path}'.");
        }

        if (body.TrimStart().StartsWith('<'))
            throw ParseXmlError(body, path);

        JObject root;
        try
        {
            root = JObject.Parse(body);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Korean FSC returned invalid JSON for '{path}'.", ex);
        }

        var response = root["response"] as JObject ?? root;
        var header = response["header"] as JObject ??
            root["header"] as JObject;
        var code = header?["resultCode"]?.ToString();
        var message = header?["resultMsg"]?.ToString();
        if (code != "00")
            throw CreateApiError(code, message, path);

        var responseBody = response["body"] as JObject ??
            root["body"] as JObject;
        if (responseBody is null)
            return new KoreanFscPage();

        var itemsToken =
            (responseBody["items"] as JObject)?["item"];
        KoreanFscPriceRow[] items;
        if (itemsToken is JArray array)
        {
            items = array.ToObject<KoreanFscPriceRow[]>(
                _serializer) ?? [];
        }
        else if (itemsToken is JObject item)
        {
            items =
            [
                item.ToObject<KoreanFscPriceRow>(
                    _serializer),
            ];
        }
        else
        {
            items = [];
        }

        return new KoreanFscPage
        {
            PageNumber = ToInt(responseBody["pageNo"]),
            PageSize = ToInt(responseBody["numOfRows"]),
            TotalCount = ToInt(responseBody["totalCount"]),
            Items = items.Where(item => item is not null).ToArray(),
        };
    }

    private async Task<byte[]> GetPayload(
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        var address = BuildAddress(path, parameters);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, address);
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);
            var payload = await response.Content.ReadAsByteArrayAsync(
                cancellationToken);

            if (IsTransient(response.StatusCode) && attempt < 3)
            {
                await Task.Delay(
                    GetRetryDelay(response, attempt),
                    cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Korean FSC request '{path}' failed " +
                    $"({(int)response.StatusCode} {response.StatusCode}): " +
                    GetErrorMessage(payload));
            }

            return payload;
        }

        throw new InvalidOperationException(
            $"Korean FSC request '{path}' exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> parameters)
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("serviceKey", _serviceKey),
        };
        pairs.AddRange(parameters.Where(
            pair => !pair.Value.IsEmpty()));

        var query = string.Join(
            "&",
            pairs.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}=" +
                Uri.EscapeDataString(pair.Value)));
        var resource = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
        return new UriBuilder(resource)
        {
            Query = query,
        }.Uri;
    }

    private static KoreanFscApiException ParseXmlError(
        string body,
        string path)
    {
        try
        {
            var xml = XDocument.Parse(body);
            var code = GetXmlValue(xml, "resultCode")
                .IsEmpty(GetXmlValue(xml, "returnReasonCode"))
                .IsEmpty(GetXmlValue(xml, "errMsg"));
            var message = GetXmlValue(xml, "resultMsg")
                .IsEmpty(GetXmlValue(xml, "returnAuthMsg"))
                .IsEmpty(GetXmlValue(xml, "errMsg"));
            return CreateApiError(code, message, path);
        }
        catch (Exception ex)
            when (ex is not KoreanFscApiException)
        {
            return new KoreanFscApiException(
                "unknown",
                $"Korean FSC request '{path}' returned unexpected XML.");
        }
    }

    private static KoreanFscApiException CreateApiError(
        string code,
        string message,
        string path)
        => new(
            code,
            $"Korean FSC request '{path}' failed " +
            $"({code.IsEmpty("unknown")}): " +
            message.IsEmpty("No error description."));

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
            return (json.SelectToken("response.header.resultMsg")
                    ?.ToString())
                .IsEmpty(json["message"]?.ToString())
                .IsEmpty(body);
        }
        catch (JsonException)
        {
        }

        try
        {
            var xml = XDocument.Parse(body);
            return GetXmlValue(xml, "resultMsg")
                .IsEmpty(GetXmlValue(xml, "returnAuthMsg"))
                .IsEmpty(GetXmlValue(xml, "errMsg"))
                .IsEmpty(body);
        }
        catch
        {
            return body;
        }
    }

    private static string GetXmlValue(
        XDocument document,
        string name)
        => document
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.EqualsIgnoreCase(name))
            ?.Value
            ?.Trim();

    private static int ToInt(JToken token)
        => int.TryParse(
            token?.ToString(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
                ? value
                : 0;

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

    private static Uri EnsureTrailingSlash(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Korean FSC API address must be an absolute HTTPS URI.",
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
