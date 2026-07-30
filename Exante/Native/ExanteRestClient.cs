namespace StockSharp.Exante.Native;

enum ExanteApiGroups
{
    Symbols,
    Ohlc,
    Feed,
    Orders,
    Summary,
    Accounts,
    Transactions,
}

sealed class ExanteRestClient : BaseLogReceiver
{
    private sealed class RateGate(TimeSpan interval) : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private DateTime _lastRequest;

        public async Task Wait(CancellationToken cancellationToken)
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
    private readonly HttpClient _streamHttp;
    private readonly Dictionary<ExanteApiGroups, RateGate> _rateGates;
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };
    private readonly int _maxAttempts;

    public ExanteRestClient(Uri address, SecureString key,
        SecureString secret, int maxAttempts)
    {
        if (address is null || !address.IsAbsoluteUri ||
            address.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                "A valid EXANTE HTTP API address is required.",
                nameof(address));

        var apiKey = key?.UnSecure().ThrowIfEmpty(nameof(key));
        var apiSecret = secret?.UnSecure().ThrowIfEmpty(nameof(secret));
        var authorization = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{apiKey}:{apiSecret}"));
        var baseAddress = new Uri(
            address.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);

        _http = CreateClient(baseAddress, authorization);
        _http.Timeout = TimeSpan.FromSeconds(45);
        _streamHttp = CreateClient(baseAddress, authorization);
        _streamHttp.Timeout = Timeout.InfiniteTimeSpan;
        _maxAttempts = Math.Max(1, maxAttempts);
        _rateGates = new()
        {
            [ExanteApiGroups.Symbols] =
                new(TimeSpan.FromSeconds(1)),
            [ExanteApiGroups.Ohlc] =
                new(TimeSpan.FromMinutes(1)),
            [ExanteApiGroups.Feed] =
                new(TimeSpan.FromMilliseconds(200)),
            [ExanteApiGroups.Orders] =
                new(TimeSpan.FromMilliseconds(200)),
            [ExanteApiGroups.Summary] =
                new(TimeSpan.FromSeconds(1)),
            [ExanteApiGroups.Accounts] =
                new(TimeSpan.FromSeconds(1)),
            [ExanteApiGroups.Transactions] =
                new(TimeSpan.FromSeconds(1)),
        };
    }

    public override string Name => "EXANTE_HTTP";

    public Task<ExanteAccount[]> GetAccounts(
        CancellationToken cancellationToken)
        => GetMany<ExanteAccount>(
            "md/3.0/accounts", ExanteApiGroups.Accounts,
            cancellationToken);

    public Task<ExanteExchange[]> GetExchanges(
        CancellationToken cancellationToken)
        => GetMany<ExanteExchange>(
            "md/3.0/exchanges", ExanteApiGroups.Symbols,
            cancellationToken);

    public Task<ExanteGroup[]> GetGroups(
        CancellationToken cancellationToken)
        => GetMany<ExanteGroup>(
            "md/3.0/groups", ExanteApiGroups.Symbols,
            cancellationToken);

    public Task<ExanteSymbol[]> GetSymbolsByExchange(string exchangeId,
        CancellationToken cancellationToken)
        => GetMany<ExanteSymbol>(
            $"md/3.0/exchanges/{Escape(exchangeId)}",
            ExanteApiGroups.Symbols, cancellationToken);

    public Task<ExanteSymbol[]> GetSymbolsByGroup(string groupId,
        CancellationToken cancellationToken)
        => GetMany<ExanteSymbol>(
            $"md/3.0/groups/{Escape(groupId)}",
            ExanteApiGroups.Symbols, cancellationToken);

    public Task<ExanteSymbol> GetSymbol(string symbolId,
        CancellationToken cancellationToken)
        => Get<ExanteSymbol>(
            $"md/3.0/symbols/{Escape(symbolId)}",
            ExanteApiGroups.Symbols, cancellationToken);

    public Task<ExanteOhlc[]> GetOhlc(string symbolId,
        TimeSpan timeFrame, DateTime? from, DateTime? to, int size,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"size={Math.Max(1, size)}",
            "type=trades",
        };
        if (from is DateTime fromValue)
            query.Add($"from={fromValue.ToUnixMilliseconds()}");
        if (to is DateTime toValue)
            query.Add($"to={toValue.ToUnixMilliseconds()}");
        return GetMany<ExanteOhlc>(
            $"md/3.0/ohlc/{Escape(symbolId)}/" +
            $"{timeFrame.ToNativeDuration()}?{query.Join("&")}",
            ExanteApiGroups.Ohlc, cancellationToken);
    }

    public Task<ExanteTradeTick[]> GetTrades(string symbolId,
        DateTime? from, DateTime? to, int size,
        CancellationToken cancellationToken)
        => GetTicks<ExanteTradeTick>(
            symbolId, from, to, size, "trades", cancellationToken);

    public Task<ExanteQuote[]> GetQuotes(string symbolId,
        DateTime? from, DateTime? to, int size,
        CancellationToken cancellationToken)
        => GetTicks<ExanteQuote>(
            symbolId, from, to, size, "quotes", cancellationToken);

    public Task<ExanteQuote> GetLastQuote(string symbolId,
        CancellationToken cancellationToken)
        => Get<ExanteQuote>(
            $"md/3.0/feed/{Escape(symbolId)}/last",
            ExanteApiGroups.Feed, cancellationToken);

    public Task<ExanteAccountSummary> GetSummary(string accountId,
        string currency, CancellationToken cancellationToken)
        => Get<ExanteAccountSummary>(
            $"md/3.0/summary/{Escape(accountId)}/{Escape(currency)}",
            ExanteApiGroups.Summary, cancellationToken);

    public Task<ExanteOrder[]> GetHistoricalOrders(string accountId,
        DateTime? from, DateTime? to, int limit,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"accountId={Escape(accountId)}",
            $"limit={Math.Clamp(limit, 1, 1000)}",
        };
        if (from is DateTime fromValue)
            query.Add("from=" + Escape(
                fromValue.ToUniversalTime().ToString(
                    "O", CultureInfo.InvariantCulture)));
        if (to is DateTime toValue)
            query.Add("to=" + Escape(
                toValue.ToUniversalTime().ToString(
                    "O", CultureInfo.InvariantCulture)));
        return GetMany<ExanteOrder>(
            $"trade/3.0/orders?{query.Join("&")}",
            ExanteApiGroups.Orders, cancellationToken);
    }

    public Task<ExanteOrder[]> GetActiveOrders(string accountId,
        int limit, CancellationToken cancellationToken)
        => GetMany<ExanteOrder>(
            $"trade/3.0/orders/active?accountId={Escape(accountId)}" +
            $"&limit={Math.Clamp(limit, 1, 1000)}",
            ExanteApiGroups.Orders, cancellationToken);

    public Task<ExanteOrder> GetOrder(string orderId,
        CancellationToken cancellationToken)
        => Get<ExanteOrder>(
            $"trade/3.0/orders/{Escape(orderId)}",
            ExanteApiGroups.Orders, cancellationToken);

    public async Task<ExanteOrder> PlaceOrder(ExantePlaceOrder order,
        CancellationToken cancellationToken)
        => (await SendMany<ExanteOrder>(
            HttpMethod.Post, "trade/3.0/orders",
            SerializeBody(order), ExanteApiGroups.Orders,
            cancellationToken)).FirstOrDefault() ??
            throw new InvalidDataException(
                "EXANTE returned no created order.");

    public Task<ExanteOrder> ModifyOrder(string orderId,
        ExanteModifyOrder modification,
        CancellationToken cancellationToken)
        => Send<ExanteOrder>(
            HttpMethod.Post,
            $"trade/3.0/orders/{Escape(orderId)}",
            SerializeBody(modification), ExanteApiGroups.Orders,
            cancellationToken);

    public Task RunQuoteStream(string symbolId, bool marketDepth,
        Func<ExanteQuote, CancellationToken, ValueTask> handler,
        Func<Exception, CancellationToken, ValueTask> errorHandler,
        CancellationToken cancellationToken)
        => RunStream(
            $"md/3.0/feed/{Escape(symbolId)}?level=" +
            (marketDepth ? "market_depth" : "best_price"),
            ExanteApiGroups.Feed,
            async (token, ct) =>
            {
                if (token.Value<string>("event") is
                    "subscription_start" or "subscription_stop")
                    return;
                var quote = token.ToObject<ExanteQuote>(
                    JsonSerializer.Create(_jsonSettings));
                if (quote is not null)
                    await handler(quote, ct);
            },
            errorHandler, cancellationToken);

    public Task RunPublicTradeStream(string symbolId,
        Func<ExanteTradeTick, CancellationToken, ValueTask> handler,
        Func<Exception, CancellationToken, ValueTask> errorHandler,
        CancellationToken cancellationToken)
        => RunStream(
            $"md/3.0/feed/trades/{Escape(symbolId)}",
            ExanteApiGroups.Feed,
            async (token, ct) =>
            {
                if (token.Value<string>("event") is
                    "subscription_start" or "subscription_stop")
                    return;
                var trade = token.ToObject<ExanteTradeTick>(
                    JsonSerializer.Create(_jsonSettings));
                if (trade is not null)
                    await handler(trade, ct);
            },
            errorHandler, cancellationToken);

    public Task RunOrderStream(
        Func<ExanteOrder, CancellationToken, ValueTask> handler,
        Func<Exception, CancellationToken, ValueTask> errorHandler,
        CancellationToken cancellationToken)
        => RunStream(
            "trade/3.0/stream/orders", ExanteApiGroups.Orders,
            async (token, ct) =>
            {
                if (token.Value<string>("event")
                    .EqualsIgnoreCase("heartbeat"))
                    return;
                var update = token.ToObject<ExanteOrderUpdate>(
                    JsonSerializer.Create(_jsonSettings));
                if (update?.Order is not null)
                    await handler(update.Order, ct);
            },
            errorHandler, cancellationToken);

    public Task RunPrivateTradeStream(
        Func<ExantePrivateTrade, CancellationToken, ValueTask> handler,
        Func<Exception, CancellationToken, ValueTask> errorHandler,
        CancellationToken cancellationToken)
        => RunStream(
            "trade/3.0/stream/trades", ExanteApiGroups.Orders,
            async (token, ct) =>
            {
                if (token.Value<string>("event")
                    .EqualsIgnoreCase("heartbeat"))
                    return;
                var trade = token.ToObject<ExantePrivateTrade>(
                    JsonSerializer.Create(_jsonSettings));
                if (trade is not null)
                    await handler(trade, ct);
            },
            errorHandler, cancellationToken);

    internal static string SerializeBody<T>(T value)
        => JsonConvert.SerializeObject(value,
            new JsonSerializerSettings
            {
                ContractResolver =
                    new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
            });

    private Task<T[]> GetTicks<T>(string symbolId,
        DateTime? from, DateTime? to, int size, string type,
        CancellationToken cancellationToken)
    {
        var query = new List<string>
        {
            $"size={Math.Max(1, size)}",
            $"type={type}",
        };
        if (from is DateTime fromValue)
            query.Add($"from={fromValue.ToUnixMilliseconds()}");
        if (to is DateTime toValue)
            query.Add($"to={toValue.ToUnixMilliseconds()}");
        return GetMany<T>(
            $"md/3.0/ticks/{Escape(symbolId)}?{query.Join("&")}",
            ExanteApiGroups.Ohlc, cancellationToken);
    }

    private Task<T> Get<T>(string path, ExanteApiGroups group,
        CancellationToken cancellationToken)
        => Send<T>(HttpMethod.Get, path, null, group,
            cancellationToken);

    private Task<T[]> GetMany<T>(string path, ExanteApiGroups group,
        CancellationToken cancellationToken)
        => SendMany<T>(HttpMethod.Get, path, null, group,
            cancellationToken);

    private async Task<T> Send<T>(HttpMethod method, string path,
        string body, ExanteApiGroups group,
        CancellationToken cancellationToken)
    {
        var payload = await SendRaw(
            method, path, body, group, cancellationToken);
        if (payload.IsEmpty())
            return default;
        var token = ParseToken(payload);
        if (token is JArray array)
            token = array.FirstOrDefault();
        return token is null
            ? default
            : token.ToObject<T>(JsonSerializer.Create(_jsonSettings));
    }

    private async Task<T[]> SendMany<T>(HttpMethod method, string path,
        string body, ExanteApiGroups group,
        CancellationToken cancellationToken)
    {
        var payload = await SendRaw(
            method, path, body, group, cancellationToken);
        if (payload.IsEmpty())
            return [];
        var token = ParseToken(payload);
        if (token is JArray array)
            return array.ToObject<T[]>(
                JsonSerializer.Create(_jsonSettings)) ?? [];
        var item = token.ToObject<T>(
            JsonSerializer.Create(_jsonSettings));
        return item is null ? [] : [item];
    }

    private async Task<string> SendRaw(HttpMethod method, string path,
        string body, ExanteApiGroups group,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await _rateGates[group].Wait(cancellationToken);
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = new StringContent(
                    body, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseContentRead,
                    cancellationToken);
            }
            catch (HttpRequestException error)
                when (attempt < _maxAttempts)
            {
                this.AddWarningLog(
                    "EXANTE {0} retry {1}: {2}",
                    method, attempt, error.Message);
                await DelayRetry(null, attempt, cancellationToken);
                continue;
            }

            using (response)
            {
                var payload = await response.Content.ReadAsStringAsync(
                    cancellationToken);
                if (response.IsSuccessStatusCode)
                    return payload;

                if (attempt < _maxAttempts &&
                    IsTransient(response.StatusCode))
                {
                    this.AddWarningLog(
                        "EXANTE {0} {1} retry {2} after HTTP {3}.",
                        method, SafePath(path), attempt,
                        (int)response.StatusCode);
                    await DelayRetry(
                        response, attempt, cancellationToken);
                    continue;
                }

                throw CreateError(response.StatusCode, payload);
            }
        }
    }

    private async Task RunStream(string path, ExanteApiGroups group,
        Func<JObject, CancellationToken, ValueTask> handler,
        Func<Exception, CancellationToken, ValueTask> errorHandler,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _rateGates[group].Wait(cancellationToken);
                using var request = new HttpRequestMessage(
                    HttpMethod.Get, path);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(
                        "application/x-json-stream"));
                using var response = await _streamHttp.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadAsStringAsync(
                        cancellationToken);
                    throw CreateError(response.StatusCode, payload);
                }

                attempt = 0;
                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(
                    stream, Encoding.UTF8, true, 4096, false);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(
                        cancellationToken);
                    if (line is null)
                        break;
                    line = line.Trim();
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                        line = line[5..].Trim();
                    if (line.IsEmpty())
                        continue;
                    var token = ParseToken(line) as JObject;
                    if (token is not null)
                        await handler(token, cancellationToken);
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                await errorHandler(error, cancellationToken);
            }

            attempt++;
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(
                    Math.Min(10000,
                        500 * (1 << Math.Min(attempt - 1, 4)))),
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static HttpClient CreateClient(
        Uri address, string authorization)
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip |
                DecompressionMethods.Deflate,
        })
        {
            BaseAddress = address,
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", authorization);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-EXANTE/1.0");
        return client;
    }

    private static JToken ParseToken(string payload)
    {
        try
        {
            return JToken.Parse(payload);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Invalid EXANTE response: {Limit(payload)}", error);
        }
    }

    private static Exception CreateError(HttpStatusCode statusCode,
        string payload)
    {
        var message = payload;
        try
        {
            var error = JsonConvert.DeserializeObject<ExanteApiError>(
                payload, new JsonSerializerSettings
                {
                    ContractResolver =
                        new CamelCasePropertyNamesContractResolver(),
                });
            message = error?.Message.IsEmpty(payload);
        }
        catch (JsonException)
        {
            // Preserve the raw payload below.
        }
        return new HttpRequestException(
            $"EXANTE HTTP {(int)statusCode} ({statusCode}): " +
            Limit(message), null, statusCode);
    }

    private static async Task DelayRetry(
        HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response?.Headers.RetryAfter?.Delta;
        if (delay is null &&
            response?.Headers.RetryAfter?.Date is DateTimeOffset date)
            delay = date - DateTimeOffset.UtcNow;
        if (delay is null || delay <= TimeSpan.Zero)
        {
            delay = TimeSpan.FromMilliseconds(
                Math.Min(10000,
                    500 * (1 << Math.Min(attempt - 1, 4))));
        }
        await Task.Delay(delay.Value, cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout ||
            (int)statusCode == 429 || (int)statusCode >= 500;

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

    private static string SafePath(string path)
        => path?.Split('?')[0];

    private static string Limit(string value)
    {
        value = value.IsEmpty("(empty response)");
        return value.Length <= 1000
            ? value : value[..1000] + "...";
    }

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _streamHttp.Dispose();

        foreach (var gate in _rateGates.Values)
            gate.Dispose();

        base.DisposeManaged();
    }
}
