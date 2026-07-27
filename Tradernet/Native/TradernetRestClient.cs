namespace StockSharp.Tradernet.Native;

enum TradernetApiGroup
{
    Default,
    Securities,
}

sealed class TradernetRestClient : BaseLogReceiver
{
    private sealed class RateGate(TimeSpan interval) : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private DateTime _lastRequest;

        public async Task Wait(
            CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var delay = interval -
                    (DateTime.UtcNow - _lastRequest);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                _lastRequest = DateTime.UtcNow;
            }
            finally
            {
                _sync.Release();
            }
        }

        public void Dispose() => _sync.Dispose();
    }

    private readonly HttpClient _http;
    private readonly string _publicKey;
    private readonly string _privateKey;
    private readonly int _maxAttempts;
    private readonly Dictionary<TradernetApiGroup, RateGate>
        _rateGates = new()
        {
            [TradernetApiGroup.Default] =
                new(TimeSpan.FromMilliseconds(250)),
            [TradernetApiGroup.Securities] =
                new(TimeSpan.FromSeconds(6)),
        };
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver =
            new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    public TradernetRestClient(Uri address, SecureString publicKey,
        SecureString privateKey, int maxAttempts)
    {
        if (address is null || !address.IsAbsoluteUri ||
            address.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "A valid Tradernet API address is required.",
                nameof(address));
        }

        _publicKey = publicKey?.UnSecure()
            .ThrowIfEmpty(nameof(publicKey));
        _privateKey = privateKey?.UnSecure()
            .ThrowIfEmpty(nameof(privateKey));
        _maxAttempts = Math.Max(1, maxAttempts);
        _http = new(new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,
        })
        {
            BaseAddress = new(
                address.AbsoluteUri.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(45),
        };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Tradernet/1.0");
    }

    public override string Name => "Tradernet_REST";

    public Task<TradernetSidInfo> GetSidInfo(
        CancellationToken cancellationToken)
        => Get<TradernetSidInfo>(
            "getSidInfo", null,
            TradernetApiGroup.Default, cancellationToken);

    public Task<TradernetSearchResponse> FindSecurities(
        string text, CancellationToken cancellationToken)
        => Get<TradernetSearchResponse>(
            "tickerFinder", new { text },
            TradernetApiGroup.Default, cancellationToken);

    public async Task<TradernetSecuritiesResponse>
        GetSecurities(int take, int skip,
            CancellationToken cancellationToken)
    {
        var token = await PostToken(
            "getAllSecurities", new
            {
                take = Math.Clamp(take, 1, 1000),
                skip = Math.Max(0, skip),
            }, TradernetApiGroup.Securities,
            cancellationToken);
        if (token is JArray array)
        {
            return new()
            {
                Securities = array.ToObject<TradernetSecurity[]>(
                    Serializer) ?? [],
            };
        }
        return token?.ToObject<TradernetSecuritiesResponse>(
            Serializer) ?? new();
    }

    public Task<TradernetSecurityInfo> GetSecurityInfo(
        string ticker, CancellationToken cancellationToken)
        => Get<TradernetSecurityInfo>(
            "getSecurityInfo",
            new { ticker, sup = true },
            TradernetApiGroup.Default, cancellationToken);

    public async Task<TradernetQuote[]> GetQuotes(
        string[] tickers,
        CancellationToken cancellationToken)
    {
        var token = await PostToken(
            "getStockQuotesJson", new { tickers },
            TradernetApiGroup.Default, cancellationToken);
        if (token is JObject obj)
            token = obj["q"] ?? token;
        return ToArray<TradernetQuote>(token);
    }

    public Task<JToken> GetHloc(string ticker,
        TimeSpan timeFrame, DateTime? from, DateTime? to,
        long? count, CancellationToken cancellationToken)
        => PostToken("getHloc", new
        {
            id = ticker,
            count = count is > 0
                ? -Math.Min(count.Value, int.MaxValue)
                : -1,
            timeframe = timeFrame.ToNativeTimeFrame(),
            date_from = from?.ToUniversalTime().ToString(
                "dd.MM.yyyy HH:mm",
                CultureInfo.InvariantCulture),
            date_to = to?.ToUniversalTime().ToString(
                "dd.MM.yyyy HH:mm",
                CultureInfo.InvariantCulture),
            intervalMode = "ClosedRay",
        }, TradernetApiGroup.Default, cancellationToken);

    public Task<JToken> GetPublicTrades(string ticker,
        CancellationToken cancellationToken)
        => PostToken("getHloc", new
        {
            id = ticker,
            timeframe = -1,
        }, TradernetApiGroup.Default, cancellationToken);

    public Task<TradernetPortfolio> GetPortfolio(
        CancellationToken cancellationToken)
        => Post<TradernetPortfolio>(
            "getPositionJson", null,
            TradernetApiGroup.Default, cancellationToken);

    public async Task<TradernetOrder[]> GetCurrentOrders(
        bool activeOnly,
        CancellationToken cancellationToken)
        => ReadOrders(await PostToken(
            "getNotifyOrderJson",
            new { active_only = activeOnly ? 1 : 0 },
            TradernetApiGroup.Default, cancellationToken));

    public async Task<TradernetOrder[]> GetHistoricalOrders(
        DateTime from, DateTime to,
        CancellationToken cancellationToken)
        => ReadOrders(await GetToken(
            "getOrdersHistory", new
            {
                from = from.ToUniversalTime().ToString(
                    "yyyy-MM-ddTHH:mm:ss",
                    CultureInfo.InvariantCulture),
                till = to.ToUniversalTime().ToString(
                    "yyyy-MM-ddTHH:mm:ss",
                    CultureInfo.InvariantCulture),
            }, TradernetApiGroup.Default, cancellationToken));

    public Task<TradernetOrderResult> PlaceOrder(
        TradernetPlaceOrder order,
        CancellationToken cancellationToken)
        => Post<TradernetOrderResult>(
            "putTradeOrder", order,
            TradernetApiGroup.Default, cancellationToken);

    public Task<TradernetOrderResult> CancelOrder(
        long orderId,
        CancellationToken cancellationToken)
        => Post<TradernetOrderResult>(
            "delTradeOrder",
            new { order_id = orderId },
            TradernetApiGroup.Default, cancellationToken);

    internal static string SerializeBody<T>(T value)
        => value is null ? string.Empty :
            JsonConvert.SerializeObject(value,
                new JsonSerializerSettings
                {
                    ContractResolver =
                        new CamelCasePropertyNamesContractResolver(),
                    NullValueHandling =
                        NullValueHandling.Ignore,
                    DateParseHandling =
                        DateParseHandling.None,
                });

    internal static string CreateSignature(
        string payload, long timestamp, string privateKey)
    {
        using var hmac = new HMACSHA256(
            Encoding.UTF8.GetBytes(
                privateKey.ThrowIfEmpty(nameof(privateKey))));
        var hash = hmac.ComputeHash(
            Encoding.UTF8.GetBytes(
                (payload ?? string.Empty) +
                timestamp.ToString(
                    CultureInfo.InvariantCulture)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<T> Get<T>(string command,
        object parameters, TradernetApiGroup group,
        CancellationToken cancellationToken)
    {
        var token = await GetToken(
            command, parameters, group, cancellationToken);
        return token is null
            ? default
            : token.ToObject<T>(Serializer);
    }

    private async Task<T> Post<T>(string command,
        object parameters, TradernetApiGroup group,
        CancellationToken cancellationToken)
    {
        var token = await PostToken(
            command, parameters, group, cancellationToken);
        return token is null
            ? default
            : token.ToObject<T>(Serializer);
    }

    private Task<JToken> GetToken(string command,
        object parameters, TradernetApiGroup group,
        CancellationToken cancellationToken)
        => Send(HttpMethod.Get, command, parameters,
            group, cancellationToken);

    private Task<JToken> PostToken(string command,
        object parameters, TradernetApiGroup group,
        CancellationToken cancellationToken)
        => Send(HttpMethod.Post, command, parameters,
            group, cancellationToken);

    private async Task<JToken> Send(HttpMethod method,
        string command, object parameters,
        TradernetApiGroup group,
        CancellationToken cancellationToken)
    {
        var body = method == HttpMethod.Get
            ? string.Empty : SerializeBody(parameters);
        var path = Uri.EscapeDataString(
            command.ThrowIfEmpty(nameof(command)));
        if (method == HttpMethod.Get &&
            parameters is not null)
        {
            var query = JObject.FromObject(
                parameters, Serializer).Properties()
                .Where(property =>
                    property.Value.Type != JTokenType.Null)
                .Select(property =>
                    $"{Uri.EscapeDataString(property.Name)}=" +
                    Uri.EscapeDataString(ToQueryValue(
                        property.Value)))
                .Join("&");
            if (!query.IsEmpty())
                path += "?" + query;
        }

        for (var attempt = 1; ; attempt++)
        {
            await _rateGates[group].Wait(cancellationToken);
            var timestamp =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = CreateSignature(
                body, timestamp, _privateKey);
            using var request =
                new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation(
                "X-NtApi-PublicKey", _publicKey);
            request.Headers.TryAddWithoutValidation(
                "X-NtApi-Sig", signature);
            request.Headers.TryAddWithoutValidation(
                "X-NtApi-Timestamp",
                timestamp.ToString(
                    CultureInfo.InvariantCulture));
            if (method != HttpMethod.Get)
            {
                request.Content = new StringContent(
                    body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(
                    request,
                    HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException error)
                when (attempt < _maxAttempts)
            {
                this.AddWarningLog(
                    "Tradernet {0} retry {1}: {2}",
                    command, attempt, error.Message);
                await DelayRetry(
                    null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var payload = await response.Content
                    .ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                    return ParseResponse(command, payload);

                if (attempt < _maxAttempts &&
                    IsTransient(response.StatusCode))
                {
                    this.AddWarningLog(
                        "Tradernet {0} retry {1} after HTTP {2}.",
                        command, attempt,
                        (int)response.StatusCode);
                    await DelayRetry(
                        response, attempt,
                        cancellationToken);
                    continue;
                }

                throw CreateError(
                    response.StatusCode, command, payload);
            }
        }
    }

    private static JToken ParseResponse(
        string command, string payload)
    {
        if (payload.IsEmpty())
            return null;

        JToken token;
        try
        {
            token = JToken.Parse(payload);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Invalid Tradernet response for {command}: " +
                Limit(payload), error);
        }

        if (token is JObject obj)
        {
            var errorText =
                obj.Value<string>("error") ??
                obj.Value<string>("errorMsg") ??
                obj.Value<string>("errMsg");
            if (!errorText.IsEmpty())
            {
                throw new InvalidOperationException(
                    $"Tradernet {command}: {errorText}");
            }
            if (obj["result"] is JToken result &&
                result.Type != JTokenType.Null)
            {
                token = result;
            }
        }
        return token;
    }

    private static TradernetOrder[] ReadOrders(JToken token)
    {
        if (token is null)
            return [];
        if (token is JObject obj)
        {
            token = obj["orders"] ??
                obj["order"] ?? token;
        }
        return ToArray<TradernetOrder>(token);
    }

    private static T[] ToArray<T>(JToken token)
    {
        if (token is null ||
            token.Type == JTokenType.Null)
            return [];
        if (token is JArray array)
        {
            return array.ToObject<T[]>(
                JsonSerializer.CreateDefault()) ?? [];
        }
        var item = token.ToObject<T>(
            JsonSerializer.CreateDefault());
        return item is null ? [] : [item];
    }

    private JsonSerializer Serializer
        => JsonSerializer.Create(_jsonSettings);

    private static string ToQueryValue(JToken value)
        => value is JValue scalar
            ? Convert.ToString(
                scalar.Value, CultureInfo.InvariantCulture)
            : value.ToString(Formatting.None);

    private static Exception CreateError(
        HttpStatusCode statusCode, string command,
        string payload)
        => new HttpRequestException(
            $"Tradernet {command} HTTP " +
            $"{(int)statusCode} ({statusCode}): " +
            Limit(payload), null, statusCode);

    private static bool IsTransient(
        HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 ||
            (int)statusCode >= 500;

    private static async Task DelayRetry(
        HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta;
        if (delay is null &&
            response?.Headers.RetryAfter?.Date is
                DateTimeOffset date)
        {
            delay = date - DateTimeOffset.UtcNow;
        }
        if (delay is null || delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(
                Math.Min(10000,
                    500 * (1 <<
                        Math.Min(attempt - 1, 4))));
        }
        await Task.Delay(delay.Value, cancellationToken);
    }

    private static string Limit(string value)
    {
        value = value.IsEmpty("(empty response)");
        return value.Length <= 1000
            ? value : value[..1000] + "...";
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        foreach (var gate in _rateGates.Values)
            gate.Dispose();
        base.DisposeManaged();
    }
}
