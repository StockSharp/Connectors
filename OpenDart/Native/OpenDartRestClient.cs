namespace StockSharp.OpenDart.Native;

sealed class OpenDartApiException : InvalidOperationException
{
    public OpenDartApiException(string status, string message)
        : base(message)
    {
        Status = status;
    }

    public string Status { get; }
}

sealed class OpenDartRestClient : BaseLogReceiver, IDisposable
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly Uri _address;
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public OpenDartRestClient(
        Uri address,
        string apiKey,
        HttpMessageHandler handler = null)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-OpenDart/1.0");
    }

    public async Task<OpenDartCompanyCode[]> GetCompanies(
        CancellationToken cancellationToken)
    {
        var payload = await GetPayload(
            "corpCode.xml",
            new Dictionary<string, string>(),
            cancellationToken);
        var document = ReadCompanyDocument(payload);
        var status = GetElementValue(document.Root, "status");
        var message = GetElementValue(document.Root, "message");

        if (!status.IsEmpty() && status != "000")
            throw CreateApiError(status, message, "corpCode.xml");

        return document
            .Descendants()
            .Where(element =>
                element.Name.LocalName.EqualsIgnoreCase("list"))
            .Select(element => new OpenDartCompanyCode
            {
                CorporationCode = GetElementValue(
                    element, "corp_code"),
                CorporationName = GetElementValue(
                    element, "corp_name"),
                EnglishName = GetElementValue(
                    element, "corp_eng_name"),
                StockCode = GetElementValue(
                    element, "stock_code"),
                ModifiedDate = GetElementValue(
                    element, "modify_date"),
            })
            .Where(company =>
                !company.CorporationCode.IsEmpty())
            .ToArray();
    }

    public async Task<OpenDartDisclosurePage> GetDisclosures(
        OpenDartDisclosureQuery query,
        CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["bgn_de"] = query.From.ToString(
                "yyyyMMdd", CultureInfo.InvariantCulture),
            ["end_de"] = query.To.ToString(
                "yyyyMMdd", CultureInfo.InvariantCulture),
            ["sort"] = "date",
            ["sort_mth"] = "desc",
            ["page_no"] = query.PageNumber.ToString(
                CultureInfo.InvariantCulture),
            ["page_count"] = query.PageSize.ToString(
                CultureInfo.InvariantCulture),
        };

        parameters.TryAdd(
            "corp_code", query.CorporationCode);
        parameters.TryAdd(
            "pblntf_ty", query.DisclosureType);
        parameters.TryAdd(
            "corp_cls", query.CorporationClass);
        if (query.FinalReportsOnly)
            parameters["last_reprt_at"] = "Y";

        var result = await GetJson<OpenDartDisclosurePage>(
            "list.json", parameters, cancellationToken);
        if (result.Status == "013")
        {
            result.Items = [];
            result.TotalCount = 0;
            result.TotalPages = 0;
        }

        return result;
    }

    public async Task<OpenDartFinancialIndicator[]> GetIndicators(
        string corporationCode,
        int businessYear,
        string reportCode,
        string categoryCode,
        CancellationToken cancellationToken)
    {
        var result =
            await GetJson<OpenDartListResponse<OpenDartFinancialIndicator>>(
                "fnlttSinglIndx.json",
                new Dictionary<string, string>
                {
                    ["corp_code"] = corporationCode,
                    ["bsns_year"] = businessYear.ToString(
                        CultureInfo.InvariantCulture),
                    ["reprt_code"] = reportCode,
                    ["idx_cl_code"] = categoryCode,
                },
                cancellationToken);

        return result.Status == "013"
            ? []
            : result.Items ?? [];
    }

    private async Task<T> GetJson<T>(
        string path,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
        where T : OpenDartResponse
    {
        var payload = await GetPayload(
            path, parameters, cancellationToken);
        var body = Encoding.UTF8.GetString(payload);

        T result;
        try
        {
            result = JsonConvert.DeserializeObject<T>(
                body, _jsonSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Open DART returned invalid JSON for '{path}'.", ex);
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                $"Open DART returned an empty response for '{path}'.");
        }

        if (result.Status is not ("000" or "013"))
        {
            throw CreateApiError(
                result.Status, result.Message, path);
        }

        return result;
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
                    $"Open DART request '{path}' failed " +
                    $"({(int)response.StatusCode} {response.StatusCode}): " +
                    GetErrorMessage(payload));
            }

            return payload;
        }

        throw new InvalidOperationException(
            $"Open DART request '{path}' exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> parameters)
    {
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("crtfc_key", _apiKey),
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
        var builder = new UriBuilder(resource)
        {
            Query = query,
        };
        return builder.Uri;
    }

    private static XDocument ReadCompanyDocument(byte[] payload)
    {
        if (payload is null || payload.Length == 0)
        {
            throw new InvalidOperationException(
                "Open DART returned an empty corporation-code file.");
        }

        if (payload.Length >= 4 &&
            payload[0] == (byte)'P' &&
            payload[1] == (byte)'K')
        {
            using var buffer = new MemoryStream(payload, writable: false);
            using var archive = new ZipArchive(
                buffer, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(item =>
                item.Name.EndsWith(
                    ".xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                throw new InvalidOperationException(
                    "Open DART corporation-code archive contains no XML file.");
            }

            using var stream = entry.Open();
            return XDocument.Load(stream);
        }

        using var source = new MemoryStream(payload, writable: false);
        return XDocument.Load(source);
    }

    private static string GetElementValue(
        XElement parent,
        string name)
        => parent?
            .Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName.EqualsIgnoreCase(name))
            ?.Value
            ?.Trim();

    private static OpenDartApiException CreateApiError(
        string status,
        string message,
        string path)
        => new(
            status,
            $"Open DART request '{path}' failed " +
            $"({status.IsEmpty("unknown")}): " +
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
            return ((string)json["message"])
                .IsEmpty(body);
        }
        catch (JsonException)
        {
        }

        try
        {
            var xml = XDocument.Parse(body);
            return GetElementValue(xml.Root, "message")
                .IsEmpty(body);
        }
        catch
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

    private static Uri EnsureTrailingSlash(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "Open DART API address must be an absolute HTTPS URI.",
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
