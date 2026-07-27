namespace StockSharp.Twse.Native;

sealed class TwseRestClient : BaseLogReceiver, IDisposable
{
    private const string _dailyPath =
        "exchangeReport/STOCK_DAY_ALL";
    private const string _valuationPath =
        "exchangeReport/BWIBBU_ALL";
    private const string _companyPath =
        "opendata/t187ap03_L";
    private const string _fundPath =
        "opendata/t187ap47_L";

    private static readonly JsonSerializer _serializer =
        JsonSerializer.Create(
            new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            });

    private readonly Uri _address;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _cacheSync = new(1, 1);

    private TwseSnapshot _snapshot;
    private DateTimeOffset _snapshotTime;
    private bool _snapshotHasProfiles;
    private bool _snapshotHasValuations;

    public TwseRestClient(
        Uri address,
        HttpMessageHandler handler = null)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(2);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-Twse/1.0");
    }

    public async Task<TwseSnapshot> GetSnapshot(
        TimeSpan cacheTimeout,
        bool includeProfiles,
        bool includeValuations,
        CancellationToken cancellationToken)
    {
        if (cacheTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheTimeout), cacheTimeout, null);
        }

        if (CanUseCache(
            cacheTimeout,
            includeProfiles,
            includeValuations))
        {
            return _snapshot;
        }

        await _cacheSync.WaitAsync(cancellationToken);
        try
        {
            if (CanUseCache(
                cacheTimeout,
                includeProfiles,
                includeValuations))
            {
                return _snapshot;
            }

            var pricesTask = GetArray<TwseDailyRow>(
                _dailyPath, cancellationToken);
            var valuationsTask = includeValuations
                ? GetArray<TwseValuationRow>(
                    _valuationPath, cancellationToken)
                : Task.FromResult<TwseValuationRow[]>([]);
            var companiesTask = includeProfiles
                ? GetObjects(_companyPath, cancellationToken)
                : Task.FromResult<JObject[]>([]);
            var fundsTask = includeProfiles
                ? GetObjects(_fundPath, cancellationToken)
                : Task.FromResult<JObject[]>([]);

            await Task.WhenAll(
                pricesTask,
                valuationsTask,
                companiesTask,
                fundsTask);

            _snapshot = new TwseSnapshot
            {
                Prices = await pricesTask,
                Valuations = await valuationsTask,
                Profiles = ParseProfiles(
                    await companiesTask,
                    await fundsTask),
            };
            _snapshotTime = DateTimeOffset.UtcNow;
            _snapshotHasProfiles = includeProfiles;
            _snapshotHasValuations = includeValuations;
            return _snapshot;
        }
        finally
        {
            _cacheSync.Release();
        }
    }

    public void ClearCache()
    {
        _snapshot = null;
        _snapshotTime = default;
        _snapshotHasProfiles = false;
        _snapshotHasValuations = false;
    }

    private bool CanUseCache(
        TimeSpan cacheTimeout,
        bool includeProfiles,
        bool includeValuations)
        => _snapshot is not null &&
            cacheTimeout > TimeSpan.Zero &&
            DateTimeOffset.UtcNow - _snapshotTime < cacheTimeout &&
            (!includeProfiles || _snapshotHasProfiles) &&
            (!includeValuations || _snapshotHasValuations);

    private async Task<T[]> GetArray<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(path, cancellationToken);
        if (token is JArray array)
        {
            return array.ToObject<T[]>(_serializer) ?? [];
        }
        if (token is JObject item)
        {
            var value = item.ToObject<T>(_serializer);
            return value is null ? [] : [value];
        }

        throw new InvalidOperationException(
            $"TWSE OpenAPI returned an unexpected payload for '{path}'.");
    }

    private async Task<JObject[]> GetObjects(
        string path,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(path, cancellationToken);
        if (token is JArray array)
            return array.OfType<JObject>().ToArray();
        if (token is JObject item)
            return [item];

        throw new InvalidOperationException(
            $"TWSE OpenAPI returned an unexpected payload for '{path}'.");
    }

    private async Task<JToken> GetToken(
        string path,
        CancellationToken cancellationToken)
    {
        var address = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));

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
                    $"TWSE OpenAPI request '{path}' failed " +
                    $"({(int)response.StatusCode} {response.StatusCode}): " +
                    GetErrorMessage(payload));
            }

            var body = Encoding.UTF8.GetString(payload ?? []);
            if (body.IsEmpty())
            {
                throw new InvalidOperationException(
                    $"TWSE OpenAPI returned an empty response for '{path}'.");
            }

            JToken token;
            try
            {
                token = JToken.Parse(body);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"TWSE OpenAPI returned invalid JSON for '{path}'.",
                    ex);
            }

            if (token is JObject envelope)
            {
                if (envelope["data"] is JArray data)
                    return data;

                var error = (envelope["error"]?.ToString())
                    .IsEmpty(envelope["message"]?.ToString())
                    .IsEmpty(envelope["title"]?.ToString());
                var status = envelope["stat"]?.ToString();
                if (!error.IsEmpty() ||
                    (!status.IsEmpty() &&
                        !status.EqualsIgnoreCase("OK")))
                {
                    throw new InvalidOperationException(
                        $"TWSE OpenAPI request '{path}' failed: " +
                        error.IsEmpty(status));
                }
            }

            return token;
        }

        throw new InvalidOperationException(
            $"TWSE OpenAPI request '{path}' exhausted its retry limit.");
    }

    private static TwseSecurityProfile[] ParseProfiles(
        IEnumerable<JObject> companies,
        IEnumerable<JObject> funds)
    {
        const string companyCode =
            "\u516c\u53f8\u4ee3\u865f";
        const string companyName =
            "\u516c\u53f8\u540d\u7a31";
        const string companyShortName =
            "\u516c\u53f8\u7c21\u7a31";
        const string englishShortName =
            "\u82f1\u6587\u7c21\u7a31";
        const string industry =
            "\u7522\u696d\u5225";
        const string listingDate =
            "\u4e0a\u5e02\u65e5\u671f";
        const string issuedShares =
            "\u5df2\u767c\u884c\u666e\u901a\u80a1\u6578\u6216TDR\u539f\u80a1\u767c\u884c\u80a1\u6578";

        const string fundCode =
            "\u57fa\u91d1\u4ee3\u865f";
        const string fundShortName =
            "\u57fa\u91d1\u7c21\u7a31";
        const string fundName =
            "\u57fa\u91d1\u4e2d\u6587\u540d\u7a31";
        const string fundEnglishName =
            "\u57fa\u91d1\u82f1\u6587\u540d\u7a31";
        const string fundType =
            "\u57fa\u91d1\u985e\u578b";
        const string issuedUnits =
            "\u767c\u884c\u55ae\u4f4d\u6578/\u8f49\u63db\u6578";

        return companies
            .Select(item => new TwseSecurityProfile
            {
                Code = GetValue(item, companyCode),
                Name = GetValue(item, companyName),
                ShortName = GetValue(item, companyShortName),
                EnglishName = GetValue(item, englishShortName),
                Class = GetValue(item, industry),
                ListingDate = GetValue(item, listingDate),
                IssueSize = GetValue(item, issuedShares),
                SecurityType = SecurityTypes.Stock,
            })
            .Concat(funds.Select(item =>
                new TwseSecurityProfile
                {
                    Code = GetValue(item, fundCode),
                    Name = GetValue(item, fundName),
                    ShortName = GetValue(item, fundShortName),
                    EnglishName = GetValue(item, fundEnglishName),
                    Class = GetValue(item, fundType),
                    ListingDate = GetValue(item, listingDate),
                    IssueSize = GetValue(item, issuedUnits),
                    SecurityType = SecurityTypes.Etf,
                }))
            .Where(profile => !profile.Code.IsEmpty())
            .GroupBy(
                profile => profile.Code,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string GetValue(JObject item, string name)
        => item?[name]?.ToString()?.Trim();

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
            return (json["error"]?.ToString())
                .IsEmpty(json["message"]?.ToString())
                .IsEmpty(json["title"]?.ToString())
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

    private static Uri EnsureTrailingSlash(Uri address)
    {
        if (!address.IsAbsoluteUri ||
            address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "TWSE OpenAPI address must be an absolute HTTPS URI.",
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
        _cacheSync.Dispose();
        base.DisposeManaged();
    }
}
