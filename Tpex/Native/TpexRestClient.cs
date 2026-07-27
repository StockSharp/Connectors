namespace StockSharp.Tpex.Native;

sealed class TpexRestClient : BaseLogReceiver, IDisposable
{
    private const string _mainboardPricesPath =
        "openapi/v1/tpex_mainboard_daily_close_quotes";
    private const string _emergingPricesPath =
        "openapi/v1/tpex_esb_latest_statistics";
    private const string _valuationPath =
        "openapi/v1/tpex_mainboard_peratio_analysis";
    private const string _mainboardProfilesPath =
        "openapi/v1/mopsfin_t187ap03_O";
    private const string _emergingProfilesPath =
        "openapi/v1/mopsfin_t187ap03_R";
    private const string _mainboardHistoryPath =
        "www/zh-tw/afterTrading/tradingStock";
    private const string _emergingHistoryPath =
        "www/zh-tw/emerging/historical";

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

    private TpexSnapshot _snapshot;
    private DateTimeOffset _snapshotTime;
    private TpexMarkets _snapshotMarket;
    private bool _snapshotHasValuations;

    public TpexRestClient(
        Uri address,
        HttpMessageHandler handler = null)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _http = handler is null
            ? new HttpClient()
            : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromMinutes(3);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "StockSharp-Tpex/1.0");
    }

    public async Task<TpexSnapshot> GetSnapshot(
        TpexMarkets market,
        bool includeValuations,
        TimeSpan cacheTimeout,
        CancellationToken cancellationToken)
    {
        ValidateMarket(market);
        if (cacheTimeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cacheTimeout), cacheTimeout, null);
        }

        if (CanUseCache(
            market, includeValuations, cacheTimeout))
        {
            return _snapshot;
        }

        await _cacheSync.WaitAsync(cancellationToken);
        try
        {
            if (CanUseCache(
                market, includeValuations, cacheTimeout))
            {
                return _snapshot;
            }

            var includeMainboard = market is
                TpexMarkets.Mainboard or TpexMarkets.All;
            var includeEmerging = market is
                TpexMarkets.Emerging or TpexMarkets.All;

            var mainboardPricesTask = includeMainboard
                ? GetArray<TpexMainboardRow>(
                    _mainboardPricesPath, cancellationToken)
                : Task.FromResult<TpexMainboardRow[]>([]);
            var mainboardProfilesTask = includeMainboard
                ? GetArray<TpexSecurityProfile>(
                    _mainboardProfilesPath, cancellationToken)
                : Task.FromResult<TpexSecurityProfile[]>([]);
            var valuationsTask =
                includeMainboard && includeValuations
                    ? GetArray<TpexValuationRow>(
                        _valuationPath, cancellationToken)
                    : Task.FromResult<TpexValuationRow[]>([]);
            var emergingPricesTask = includeEmerging
                ? GetArray<TpexEmergingRow>(
                    _emergingPricesPath, cancellationToken)
                : Task.FromResult<TpexEmergingRow[]>([]);
            var emergingProfilesTask = includeEmerging
                ? GetArray<TpexSecurityProfile>(
                    _emergingProfilesPath, cancellationToken)
                : Task.FromResult<TpexSecurityProfile[]>([]);

            await Task.WhenAll(
                mainboardPricesTask,
                mainboardProfilesTask,
                valuationsTask,
                emergingPricesTask,
                emergingProfilesTask);

            var emergingProfiles = await emergingProfilesTask;
            foreach (var profile in emergingProfiles)
                profile.IsEmerging = true;

            _snapshot = new TpexSnapshot
            {
                MainboardPrices = await mainboardPricesTask,
                MainboardProfiles = await mainboardProfilesTask,
                Valuations = await valuationsTask,
                EmergingPrices = await emergingPricesTask,
                EmergingProfiles = emergingProfiles,
            };
            _snapshotTime = DateTimeOffset.UtcNow;
            _snapshotMarket = market;
            _snapshotHasValuations = includeValuations;
            return _snapshot;
        }
        finally
        {
            _cacheSync.Release();
        }
    }

    public Task<TpexHistoryRow[]> GetMainboardHistory(
        string symbol,
        DateTime month,
        CancellationToken cancellationToken)
        => GetHistory(
            _mainboardHistoryPath,
            new Dictionary<string, string>
            {
                ["code"] = symbol.ThrowIfEmpty(nameof(symbol)),
                ["date"] = month.ToString(
                    "yyyy/MM/01", CultureInfo.InvariantCulture),
                ["response"] = "json",
            },
            isEmerging: false,
            cancellationToken);

    public Task<TpexHistoryRow[]> GetEmergingHistory(
        string symbol,
        DateTime month,
        CancellationToken cancellationToken)
        => GetHistory(
            _emergingHistoryPath,
            new Dictionary<string, string>
            {
                ["date"] = month.ToString(
                    "yyyy/MM", CultureInfo.InvariantCulture),
                ["code"] = symbol.ThrowIfEmpty(nameof(symbol)),
                ["response"] = "json",
            },
            isEmerging: true,
            cancellationToken);

    private bool CanUseCache(
        TpexMarkets market,
        bool includeValuations,
        TimeSpan cacheTimeout)
        => _snapshot is not null &&
            _snapshotMarket == market &&
            (!includeValuations || _snapshotHasValuations) &&
            cacheTimeout > TimeSpan.Zero &&
            DateTimeOffset.UtcNow - _snapshotTime < cacheTimeout;

    private async Task<T[]> GetArray<T>(
        string path,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(
            path, null, cancellationToken);
        if (token is JArray array)
            return array.ToObject<T[]>(_serializer) ?? [];

        if (token is JObject envelope &&
            envelope["data"] is JArray data)
        {
            return data.ToObject<T[]>(_serializer) ?? [];
        }

        var error = (token as JObject)?["message"]?.ToString();
        error = error.IsEmpty(
            (token as JObject)?["stat"]?.ToString());
        throw new InvalidOperationException(
            $"TPEx returned an unexpected payload for '{path}': " +
            error.IsEmpty("no data array"));
    }

    private async Task<TpexHistoryRow[]> GetHistory(
        string path,
        IReadOnlyDictionary<string, string> query,
        bool isEmerging,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(
            path, query, cancellationToken);
        if (token is not JObject root)
        {
            throw new InvalidOperationException(
                $"TPEx returned an unexpected history payload for '{path}'.");
        }

        var table = (root["tables"] as JArray)?
            .OfType<JObject>()
            .FirstOrDefault();
        var rows = table?["data"] as JArray;
        if (rows is null)
        {
            throw new InvalidOperationException(
                $"TPEx history request '{path}' failed: " +
                (root["stat"]?.ToString())
                    .IsEmpty("missing data table"));
        }
        if (rows.Count == 0)
            return [];

        return rows
            .OfType<JArray>()
            .Select(row => isEmerging
                ? ParseEmergingHistoryRow(row)
                : ParseMainboardHistoryRow(row))
            .Where(row => row is not null)
            .ToArray();
    }

    private static TpexHistoryRow ParseMainboardHistoryRow(
        JArray values)
    {
        if (values.Count < 9)
            return null;

        return new TpexHistoryRow
        {
            Date = GetValue(values, 0),
            Volume = GetValue(values, 1),
            Turnover = GetValue(values, 2),
            Open = GetValue(values, 3),
            High = GetValue(values, 4),
            Low = GetValue(values, 5),
            Close = GetValue(values, 6),
            Change = GetValue(values, 7),
            TradesCount = GetValue(values, 8),
            VolumeMultiplier = 1000,
            TurnoverMultiplier = 1000,
        };
    }

    private static TpexHistoryRow ParseEmergingHistoryRow(
        JArray values)
    {
        if (values.Count < 13)
            return null;

        return new TpexHistoryRow
        {
            IsEmerging = true,
            Date = GetValue(values, 0),
            Volume = GetValue(values, 1),
            Turnover = GetValue(values, 2),
            High = GetValue(values, 3),
            Low = GetValue(values, 4),
            Close = GetValue(values, 5),
            TradesCount = GetValue(values, 6),
            SecondaryVolume = GetValue(values, 7),
            SecondaryTurnover = GetValue(values, 8),
            SecondaryHigh = GetValue(values, 9),
            SecondaryLow = GetValue(values, 10),
            SecondaryAverage = GetValue(values, 11),
            SecondaryTradesCount = GetValue(values, 12),
        };
    }

    private async Task<JToken> GetToken(
        string path,
        IReadOnlyDictionary<string, string> query,
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
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
                var payload =
                    await response.Content.ReadAsByteArrayAsync(
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
                        $"TPEx request '{path}' failed " +
                        $"({(int)response.StatusCode} {response.StatusCode}): " +
                        GetErrorMessage(payload),
                        null,
                        response.StatusCode);
                }

                var body = Encoding.UTF8.GetString(payload ?? []);
                if (body.IsEmpty())
                {
                    throw new InvalidOperationException(
                        $"TPEx returned an empty response for '{path}'.");
                }

                try
                {
                    return JToken.Parse(body);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"TPEx returned invalid JSON for '{path}'.", ex);
                }
            }
            catch (HttpRequestException ex)
                when (ex.StatusCode is null && attempt < 3)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                    cancellationToken);
            }
        }

        throw new InvalidOperationException(
            $"TPEx request '{path}' exhausted its retry limit.");
    }

    private Uri BuildAddress(
        string path,
        IReadOnlyDictionary<string, string> query)
    {
        var resource = new Uri(
            _address, path.ThrowIfEmpty(nameof(path)));
        if (query is null || query.Count == 0)
            return resource;

        var queryString = string.Join(
            "&",
            query
                .Where(pair => !pair.Value.IsEmpty())
                .Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    Uri.EscapeDataString(pair.Value)));
        return new UriBuilder(resource)
        {
            Query = queryString,
        }.Uri;
    }

    private static string GetValue(JArray values, int index)
        => index >= 0 && index < values.Count
            ? values[index]?.ToString()?.Trim()
            : null;

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
            return (json["message"]?.ToString())
                .IsEmpty(json["stat"]?.ToString())
                .IsEmpty(body);
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static void ValidateMarket(TpexMarkets market)
    {
        if (market is not
            TpexMarkets.Mainboard and not
            TpexMarkets.Emerging and not
            TpexMarkets.All)
        {
            throw new ArgumentOutOfRangeException(
                nameof(market), market, null);
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
                "TPEx address must be an absolute HTTPS URI.",
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
