namespace StockSharp.Tapbit.Native;

sealed class TapbitRestClient : BaseLogReceiver
{
    private sealed class RateGate : IDisposable
    {
        private readonly SemaphoreSlim _sync = new(1, 1);
        private readonly TimeSpan _interval;
        private DateTime _nextRequest;

        public RateGate(TimeSpan interval) => _interval = interval;

        public async ValueTask WaitAsync(
            CancellationToken cancellationToken)
        {
            await _sync.WaitAsync(cancellationToken);
            try
            {
                var delay = _nextRequest - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken);
                _nextRequest = DateTime.UtcNow + _interval;
            }
            finally
            {
                _sync.Release();
            }
        }

        public void Dispose() => _sync.Dispose();
    }

    private const int _maximumReadAttempts = 4;
    private const int _maximumResponseBytes = 16 * 1024 * 1024;
    private readonly Uri _spotEndpoint;
    private readonly Uri _futuresEndpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly string _apiKey;
    private readonly byte[] _apiSecret;
    private readonly RateGate _spotPublicGate = new(
        TimeSpan.FromMilliseconds(250));
    private readonly RateGate _futuresPublicGate = new(
        TimeSpan.FromMilliseconds(100));
    private readonly RateGate _privateGate = new(TimeSpan.FromSeconds(1));
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };

    public TapbitRestClient(string spotEndpoint, string futuresEndpoint,
        SecureString key, SecureString secret)
    {
        _spotEndpoint = ValidateEndpoint(spotEndpoint, "Spot");
        _futuresEndpoint = ValidateEndpoint(futuresEndpoint, "futures");
        _apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
        var secretText = secret.IsEmpty() ? null : secret.UnSecure().Trim();
        if (_apiKey.IsEmpty() != secretText.IsEmpty())
            throw new ArgumentException(
                "Tapbit API key and secret must be configured together.");
        if (!secretText.IsEmpty())
            _apiSecret = Encoding.UTF8.GetBytes(secretText);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Tapbit-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Tapbit_REST";

    public bool IsPrivateAvailable => !_apiKey.IsEmpty();

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _spotPublicGate.Dispose();
        _futuresPublicGate.Dispose();
        _privateGate.Dispose();
        if (_apiSecret is not null)
            CryptographicOperations.ZeroMemory(_apiSecret);
        base.DisposeManaged();
    }

    public async ValueTask<TapbitInstrument[]> GetSpotProductsAsync(
        CancellationToken cancellationToken)
    {
        var products = await SendPublicAsync<TapbitSpotProduct[]>(
            TapbitProductTypes.Spot, HttpMethod.Get,
            "/api/spot/instruments/trade_pair_list", null,
            cancellationToken);
        return [.. (products ?? []).Where(static product =>
            product?.Symbol.IsEmpty() == false).Select(static product =>
            new TapbitInstrument
            {
                ProductType = TapbitProductTypes.Spot,
                Symbol = product.Symbol.NormalizeTapbitSymbol(),
                StreamSymbol = product.Symbol.ToStreamSymbol(),
                BaseAsset = product.BaseAsset?.ToUpperInvariant(),
                QuoteAsset = product.QuoteAsset?.ToUpperInvariant(),
                PricePrecision = product.PricePrecision.ToInt() ?? 0,
                VolumePrecision = product.VolumePrecision.ToInt() ?? 0,
                PriceStep = (product.PricePrecision.ToInt() ?? 0)
                    .PrecisionToStep(),
                VolumeStep = (product.VolumePrecision.ToInt() ?? 0)
                    .PrecisionToStep(),
                MinimumVolume = product.MinimumVolume.ToDecimal(),
                MinimumNotional = product.MinimumNotional.ToDecimal(),
            })];
    }

    public async ValueTask<TapbitInstrument[]> GetFuturesProductsAsync(
        CancellationToken cancellationToken)
    {
        var products = await SendPublicAsync<TapbitFuturesProduct[]>(
            TapbitProductTypes.Futures, HttpMethod.Get,
            "/api/usdt/instruments/list", null, cancellationToken);
        return [.. (products ?? []).Where(static product =>
            product?.Symbol.IsEmpty() == false).Select(static product =>
            new TapbitInstrument
            {
                ProductType = TapbitProductTypes.Futures,
                Symbol = product.Symbol.NormalizeTapbitSymbol(),
                StreamSymbol = product.Symbol.NormalizeTapbitSymbol(),
                BaseAsset = product.Symbol.NormalizeTapbitSymbol()
                    .Replace("-SWAP", string.Empty,
                        StringComparison.OrdinalIgnoreCase),
                QuoteAsset = "USDT",
                PricePrecision = product.PricePrecision.ToInt() ?? 0,
                VolumePrecision = 0,
                PriceStep = product.PriceStep.ToDecimal(),
                VolumeStep = 1m,
                MinimumVolume = product.MinimumVolume.ToDecimal(),
                MaximumVolume = product.MaximumVolume.ToDecimal(),
                Multiplier = product.Multiplier.ToDecimal(),
                MaximumLeverage = product.MaximumLeverage.ToInt(),
            })];
    }

    public ValueTask<TapbitSpotTicker> GetSpotTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<TapbitSpotTicker>(TapbitProductTypes.Spot,
            HttpMethod.Get, "/api/spot/instruments/ticker_one",
            new TapbitQueryBuilder().Add("instrument_id", symbol).ToString(),
            cancellationToken);

    public ValueTask<TapbitFuturesTicker> GetFuturesTickerAsync(string symbol,
        CancellationToken cancellationToken)
        => SendPublicAsync<TapbitFuturesTicker>(TapbitProductTypes.Futures,
            HttpMethod.Get, "/api/usdt/instruments/ticker_one",
            new TapbitQueryBuilder().Add("instrument_id", symbol).ToString(),
            cancellationToken);

    public ValueTask<TapbitOrderBook> GetOrderBookAsync(
        TapbitInstrument instrument, int depth,
        CancellationToken cancellationToken)
        => SendPublicAsync<TapbitOrderBook>(instrument.ProductType,
            HttpMethod.Get, instrument.ProductType == TapbitProductTypes.Spot
                ? "/api/spot/instruments/depth"
                : "/api/usdt/instruments/depth",
            new TapbitQueryBuilder()
                .Add("instrument_id", instrument.ToRestSymbol())
                .Add("depth", NormalizeDepth(depth)).ToString(),
            cancellationToken);

    public async ValueTask<TapbitPublicTrade[]> GetPublicTradesAsync(
        TapbitInstrument instrument, CancellationToken cancellationToken)
    {
        var path = instrument.ProductType == TapbitProductTypes.Spot
            ? "/api/spot/instruments/trade_list"
            : "/api/usdt/instruments/trade_list";
        var query = new TapbitQueryBuilder()
            .Add("instrument_id", instrument.ToRestSymbol()).ToString();
        if (instrument.ProductType == TapbitProductTypes.Spot)
        {
            var items = await SendPublicAsync<TapbitSpotTrade[]>(
                instrument.ProductType, HttpMethod.Get, path, query,
                cancellationToken);
            return [.. (items ?? []).Where(static item => item is not null)
                .Select(item => new TapbitPublicTrade
                {
                    ProductType = instrument.ProductType,
                    Symbol = instrument.Symbol,
                    Price = item.Price,
                    Volume = item.Volume,
                    Side = item.Side.ToStockSharp(),
                    Timestamp = item.Timestamp,
                })];
        }
        else
        {
            var items = await SendPublicAsync<TapbitFuturesTrade[]>(
                instrument.ProductType, HttpMethod.Get, path, query,
                cancellationToken);
            return [.. (items ?? []).Where(static item => item is not null)
                .Select(item => new TapbitPublicTrade
                {
                    ProductType = instrument.ProductType,
                    Symbol = instrument.Symbol,
                    Price = item.Price,
                    Volume = item.Volume,
                    Side = item.Side.ToStockSharp(),
                    Timestamp = item.Timestamp,
                })];
        }
    }

    public ValueTask<TapbitCandle[]> GetCandlesAsync(
        TapbitInstrument instrument, TimeSpan timeFrame, DateTime from,
        DateTime to, CancellationToken cancellationToken)
    {
        var path = instrument.ProductType == TapbitProductTypes.Spot
            ? "/api/spot/instruments/candles"
            : "/api/usdt/instruments/candles";
        var query = new TapbitQueryBuilder()
            .Add("instrument_id", instrument.ToRestSymbol())
            .Add("start_time", ToUnixSeconds(from))
            .Add("end_time", ToUnixSeconds(to))
            .Add("period", timeFrame.ToInterval()).ToString();
        return SendPublicAsync<TapbitCandle[]>(instrument.ProductType,
            HttpMethod.Get, path, query, cancellationToken);
    }

    public ValueTask<TapbitBalance[]> GetBalancesAsync(
        CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitBalance[]>(HttpMethod.Get,
            "/api/v1/spot/account/list", null, null, cancellationToken);

    public ValueTask<TapbitOrderId> PlaceOrderAsync(
        TapbitPlaceOrderRequest request,
        CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitOrderId>(HttpMethod.Post,
            "/api/v1/spot/order", null, Serialize(request),
            cancellationToken);

    public ValueTask<TapbitOrderId> CancelOrderAsync(string orderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitOrderId>(HttpMethod.Post,
            "/api/v1/spot/cancel_order", null, Serialize(
                new TapbitOrderIdRequest { OrderId = orderId }),
            cancellationToken);

    public ValueTask<TapbitBatchCancelResult[]> CancelOrdersAsync(
        string[] orderIds, CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitBatchCancelResult[]>(HttpMethod.Post,
            "/api/v1/spot/batch_cancel_order", null, Serialize(
                new TapbitBatchCancelRequest { OrderIds = orderIds }),
            cancellationToken);

    public ValueTask<TapbitOrder[]> GetOpenOrdersAsync(string symbol,
        string nextOrderId, CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitOrder[]>(HttpMethod.Get,
            "/api/v1/spot/open_order_list", new TapbitQueryBuilder()
                .Add("instrument_id", symbol)
                .Add("next_order_id", nextOrderId).ToString(), null,
            cancellationToken);

    public ValueTask<TapbitOrder[]> GetClosedOrdersAsync(string symbol,
        string nextOrderId, CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitOrder[]>(HttpMethod.Get,
            "/api/v1/spot/closed_order_list", new TapbitQueryBuilder()
                .Add("instrument_id", symbol)
                .Add("next_order_id", nextOrderId).ToString(), null,
            cancellationToken);

    public ValueTask<TapbitOrder> GetOrderAsync(string orderId,
        CancellationToken cancellationToken)
        => SendPrivateAsync<TapbitOrder>(HttpMethod.Get,
            "/api/v1/spot/order_info", new TapbitQueryBuilder()
                .Add("order_id", orderId).ToString(), null,
            cancellationToken);

    private static int NormalizeDepth(int value)
        => value <= 5 ? 5 : value <= 10 ? 10 : value <= 50 ? 50 : 100;

    private static long ToUnixSeconds(DateTime value)
        => Convert.ToInt64(Math.Floor(
            value.ToUniversalTime().ToUnix(false) / 1000d));

    private string Serialize<TRequest>(TRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return JsonConvert.SerializeObject(request, _jsonSettings);
    }

    private ValueTask<TResponse> SendPublicAsync<TResponse>(
        TapbitProductTypes productType, HttpMethod method, string path,
        string query, CancellationToken cancellationToken)
        => SendAsync<TResponse>(productType, method, path, query, null, false,
            cancellationToken);

    private ValueTask<TResponse> SendPrivateAsync<TResponse>(
        HttpMethod method, string path, string query, string body,
        CancellationToken cancellationToken)
    {
        EnsurePrivateAvailable();
        return SendAsync<TResponse>(TapbitProductTypes.Spot, method, path,
            query, body, true, cancellationToken);
    }

    private async ValueTask<TResponse> SendAsync<TResponse>(
        TapbitProductTypes productType, HttpMethod method, string path,
        string query, string body, bool isPrivate,
        CancellationToken cancellationToken)
    {
        var queryString = query.IsEmpty() ? string.Empty : $"?{query}";
        var endpoint = productType == TapbitProductTypes.Spot
            ? _spotEndpoint
            : _futuresEndpoint;
        var gate = isPrivate ? _privateGate
            : productType == TapbitProductTypes.Spot
                ? _spotPublicGate
                : _futuresPublicGate;
        var isRead = method == HttpMethod.Get;
        for (var attempt = 0; ; attempt++)
        {
            await gate.WaitAsync(cancellationToken);
            using var request = new HttpRequestMessage(method,
                new Uri(endpoint, (path + queryString).TrimStart('/')));
            if (!body.IsEmpty())
                request.Content = new StringContent(body, Encoding.UTF8,
                    "application/json");
            if (isPrivate)
                AddAuthentication(request, path, query, body);
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (isRead && attempt < _maximumReadAttempts - 1 &&
                IsTransient(response.StatusCode))
            {
                await DelayRetryAsync(response, attempt, cancellationToken);
                continue;
            }
            return DeserializeResponse<TResponse>(response.StatusCode,
                responseBody);
        }
    }

    private void AddAuthentication(HttpRequestMessage request, string path,
        string query, string body)
    {
        var milliseconds = DateTime.UtcNow.ToUnix(false);
        var timestamp = (milliseconds / 1000d).ToString("0.000",
            CultureInfo.InvariantCulture);
        var prehash = timestamp + request.Method.Method.ToUpperInvariant() +
            path + (query.IsEmpty() ? string.Empty : $"?{query}") +
            (body ?? string.Empty);
        var signature = HMACSHA256.HashData(_apiSecret,
            Encoding.UTF8.GetBytes(prehash));
        try
        {
            request.Headers.TryAddWithoutValidation("ACCESS-KEY", _apiKey);
            request.Headers.TryAddWithoutValidation("ACCESS-SIGN",
                Convert.ToHexString(signature).ToLowerInvariant());
            request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP",
                timestamp);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(signature);
        }
    }

    private TResponse DeserializeResponse<TResponse>(HttpStatusCode status,
        string body)
    {
        TapbitResponse<TResponse> response = null;
        try
        {
            if (!body.IsEmpty())
                response = JsonConvert.DeserializeObject<
                    TapbitResponse<TResponse>>(body, _jsonSettings);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                "Tapbit returned an unexpected response shape.", error);
        }
        if ((int)status is < 200 or >= 300 || response?.Code != 200)
            throw CreateApiError(status, response?.Code, response?.Message,
                body);
        return response.Data;
    }

    private static Exception CreateApiError(HttpStatusCode status, int? code,
        string message, string body)
    {
        if (message.IsEmpty())
        {
            message = body?.Trim();
            if (message?.Length > 512)
                message = message[..512];
        }
        if (message.IsEmpty())
            message = "The API rejected the request.";
        return new TapbitApiException(status, code,
            $"Tapbit HTTP {(int)status} " +
            $"({code?.ToString() ?? "unknown"}): {message}");
    }

    private void EnsurePrivateAvailable()
    {
        if (!IsPrivateAvailable)
            throw new InvalidOperationException(
                "Tapbit API key and secret are required for private Spot " +
                "operations.");
    }

    private static Uri ValidateEndpoint(string value, string section)
    {
        value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/') + "/";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            !endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
            throw new ArgumentException(
                $"Tapbit {section} REST endpoint must be an absolute HTTPS " +
                "URI.", nameof(value));
        return endpoint;
    }

    private static bool IsTransient(HttpStatusCode status)
        => status == (HttpStatusCode)429 || (int)status >= 500;

    private static async ValueTask DelayRetryAsync(
        HttpResponseMessage response, int attempt,
        CancellationToken cancellationToken)
    {
        var delay = response.Headers.RetryAfter?.Delta ??
            (response.Headers.RetryAfter?.Date?.UtcDateTime -
                DateTime.UtcNow) ??
            TimeSpan.FromMilliseconds(250 * (1 << attempt));
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;
        if (delay > TimeSpan.FromSeconds(5))
            delay = TimeSpan.FromSeconds(5);
        await Task.Delay(delay, cancellationToken);
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "Tapbit response exceeds the 16 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "Tapbit response exceeds the 16 MiB safety limit.");
            target.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
