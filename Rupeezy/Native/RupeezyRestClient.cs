namespace StockSharp.Rupeezy.Native;

sealed class RupeezyRestClient : BaseLogReceiver
{
    private const int _masterColumnCount = 16;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _applicationId;
    private readonly string _apiKey;
    private readonly Uri _masterAddress;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private RupeezyInstrument[] _instruments;
    private IReadOnlyDictionary<string, RupeezyInstrument> _instrumentsByKey;

    public RupeezyRestClient(
        string applicationId,
        SecureString apiKey,
        SecureString accessToken,
        Uri address,
        Uri masterAddress)
    {
        _applicationId = applicationId.ThrowIfEmpty(nameof(applicationId));
        _apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).UnSecure();
        AccessToken = accessToken?.UnSecure();
        _masterAddress = masterAddress ?? throw new ArgumentNullException(nameof(masterAddress));
        _httpClient = new()
        {
            BaseAddress = address ?? throw new ArgumentNullException(nameof(address)),
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Rupeezy/1.0");
    }

    public override string Name => nameof(Rupeezy) + "_" + nameof(RupeezyRestClient);

    public string AccessToken { get; private set; }

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<RupeezyLoginResult> Authenticate(
        SecureString authCode,
        CancellationToken cancellationToken)
    {
        if (!AccessToken.IsEmpty())
        {
            return new()
            {
                AccessToken = AccessToken,
            };
        }

        var code = authCode.ThrowIfEmpty(nameof(authCode)).UnSecure();
        var checksum = CreateChecksum(_applicationId, code, _apiKey);
        var data = await SendCore(
            HttpMethod.Post,
            "user/session",
            new JObject
            {
                ["checksum"] = checksum,
                ["applicationId"] = _applicationId,
                ["token"] = code,
            },
            false,
            cancellationToken);
        AccessToken = data.GetText("access_token")
            .ThrowIfEmpty(nameof(RupeezyLoginResult.AccessToken));
        return new()
        {
            AccessToken = AccessToken,
            UserId = data.GetText("user_id"),
        };
    }

    public async Task<RupeezyInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            _instruments = await DownloadInstruments(cancellationToken);
            _instrumentsByKey = _instruments
                .GroupBy(
                    instrument => instrument.Exchange.ToInstrumentKey(instrument.Token),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            return _instruments;
        }
        finally
        {
            _instrumentLock.Release();
        }
    }

    public async Task<RupeezyInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(instrumentKey, out var instrument)
            ? instrument
            : null;
    }

    public async Task<string> PlaceOrder(
        string exchange,
        string token,
        Sides side,
        RupeezyProducts product,
        OrderTypes orderType,
        decimal quantity,
        decimal price,
        decimal triggerPrice,
        decimal disclosedQuantity,
        TimeInForce? timeInForce,
        bool isAfterMarket,
        string orderIdentifier,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Post,
            "trading/orders/regular",
            new JObject
            {
                ["exchange"] = exchange,
                ["token"] = token,
                ["transaction_type"] = side.ToNative(),
                ["product"] = product.ToNative(),
                ["variety"] = orderType.ToVariety(price),
                ["quantity"] = quantity,
                ["price"] = orderType == OrderTypes.Market ? 0 : price,
                ["trigger_price"] = triggerPrice,
                ["disclosed_quantity"] = disclosedQuantity,
                ["validity"] = timeInForce.ToValidity(),
                ["is_amo"] = isAfterMarket,
                ["order_identifier"] = orderIdentifier,
            },
            cancellationToken);
        return data.GetText("order_id")
            .ThrowIfEmpty(nameof(RupeezyOrder.OrderId));
    }

    public async Task<string> ModifyOrder(
        string orderId,
        OrderTypes orderType,
        decimal quantity,
        decimal tradedQuantity,
        decimal price,
        decimal triggerPrice,
        decimal disclosedQuantity,
        TimeInForce? timeInForce,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Put,
            $"trading/orders/regular/{Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId)))}",
            new JObject
            {
                ["variety"] = orderType.ToVariety(price),
                ["quantity"] = quantity,
                ["traded_quantity"] = tradedQuantity,
                ["price"] = orderType == OrderTypes.Market ? 0 : price,
                ["trigger_price"] = triggerPrice,
                ["disclosed_quantity"] = disclosedQuantity,
                ["validity"] = timeInForce.ToValidity(),
            },
            cancellationToken);
        return data.GetText("order_id").IsEmpty(orderId);
    }

    public async Task<string> CancelOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Delete,
            $"trading/orders/regular/{Uri.EscapeDataString(orderId.ThrowIfEmpty(nameof(orderId)))}",
            null,
            cancellationToken);
        return data.GetText("order_id").IsEmpty(orderId);
    }

    public async Task<RupeezyOrder[]> GetOrders(CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            "trading/orders?limit=1000&offset=1",
            null,
            cancellationToken);
        return ParseArray<RupeezyOrder>(
            data.GetValueIgnoreCase("orders") ?? data);
    }

    public async Task<RupeezyTrade[]> GetTrades(CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            "trading/trades",
            null,
            cancellationToken);
        return ParseArray<RupeezyTrade>(
            data.GetValueIgnoreCase("trades") ?? data);
    }

    public async Task<RupeezyPosition[]> GetPositions(
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            "trading/portfolio/positions",
            null,
            cancellationToken);
        return ParseArray<RupeezyPosition>(
            data.GetValueIgnoreCase("net") ?? data);
    }

    public async Task<RupeezyHolding[]> GetHoldings(
        CancellationToken cancellationToken)
        => ParseArray<RupeezyHolding>(
            await SendAuthenticated(
                HttpMethod.Get,
                "trading/portfolio/holdings",
                null,
                cancellationToken));

    public async Task<RupeezyFund[]> GetFunds(CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            "user/funds",
            null,
            cancellationToken);
        var result = new List<RupeezyFund>();
        foreach (var segment in new[] { "nse", "mcx" })
        {
            var value = data.GetValueIgnoreCase(segment);
            if (value is not JObject)
                continue;
            result.Add(new()
            {
                Segment = segment.ToUpperInvariant(),
                Deposit = value.GetDecimal("deposit") ?? 0,
                Collateral = value.GetDecimal("collateral") ?? 0,
                TradingPower = value.GetDecimal("total_trading_power") ?? 0,
                Utilization = value.GetDecimal("total_utilization") ?? 0,
                Available = value.GetDecimal("net_available") ?? 0,
                Withdrawable = value.GetDecimal("withdrawable_balance") ?? 0,
                RealizedPnL = value.GetDecimal("booked_profit") ?? 0,
                UnrealizedPnL = value.GetDecimal("mtm_and_booked_loss") ?? 0,
            });
        }
        return [.. result];
    }

    public async Task<RupeezyCandle[]> GetCandles(
        string exchange,
        string token,
        TimeSpan timeFrame,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        if (!RupeezyExtensions.TimeFrames.TryGetValue(timeFrame, out var resolution))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeFrame),
                timeFrame,
                "Rupeezy does not support this candle time frame.");
        }

        var end = (to ?? DateTime.UtcNow).ToUniversalTime();
        var defaultRange = timeFrame >= TimeSpan.FromDays(1)
            ? TimeSpan.FromDays(365)
            : TimeSpan.FromDays(30);
        var start = (from ?? end - defaultRange).ToUniversalTime();
        var path =
            $"data/history?exchange={Uri.EscapeDataString(exchange)}" +
            $"&token={Uri.EscapeDataString(token)}" +
            $"&to={new DateTimeOffset(end).ToUnixTimeSeconds()}" +
            $"&from={new DateTimeOffset(start).ToUnixTimeSeconds()}" +
            $"&resolution={Uri.EscapeDataString(resolution)}";
        var data = await SendAuthenticated(
            HttpMethod.Get,
            path,
            null,
            cancellationToken);
        return ParseCandles(data);
    }

    internal static JToken ParseResponse(string content, string operation)
    {
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Rupeezy returned an empty response for {operation}.");
        }

        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                $"Rupeezy returned invalid JSON for {operation}.",
                ex);
        }

        if (root is not JObject obj)
            return root;

        var status = obj.GetText("status", "s");
        if (status.IsEmpty())
            return obj.GetValueIgnoreCase("data") ?? obj;

        if (status.EqualsIgnoreCase("success") ||
            status.EqualsIgnoreCase("ok") ||
            status.EqualsIgnoreCase("true") ||
            status.EqualsIgnoreCase("no_data"))
            return obj.GetValueIgnoreCase("data") ?? obj;

        var message = obj.GetText("message", "error", "error_reason")
            .IsEmpty(obj.GetText("code"))
            .IsEmpty(status)
            .IsEmpty("Unknown API error.");
        throw new InvalidOperationException(
            $"Rupeezy {operation} error: {message}");
    }

    internal static string CreateChecksum(
        string applicationId,
        string authCode,
        string apiKey)
        => Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    applicationId.ThrowIfEmpty(nameof(applicationId)) +
                    authCode.ThrowIfEmpty(nameof(authCode)) +
                    apiKey.ThrowIfEmpty(nameof(apiKey)))))
            .ToLowerInvariant();

    internal static T[] ParseArray<T>(JToken data)
    {
        if (data == null || data.Type is JTokenType.Null or JTokenType.Undefined)
            return [];

        var serializer = JsonSerializer.Create(_jsonSettings);
        if (data is JArray array)
            return array.ToObject<T[]>(serializer) ?? [];
        return data.Type == JTokenType.Object
            ? [data.ToObject<T>(serializer)]
            : [];
    }

    internal static RupeezyCandle[] ParseCandles(JToken data)
    {
        if (data is not JObject obj)
            return [];

        var times = obj.GetValueIgnoreCase("t") as JArray;
        var opens = obj.GetValueIgnoreCase("o") as JArray;
        var highs = obj.GetValueIgnoreCase("h") as JArray;
        var lows = obj.GetValueIgnoreCase("l") as JArray;
        var closes = obj.GetValueIgnoreCase("c") as JArray;
        var volumes = obj.GetValueIgnoreCase("v") as JArray;
        var count = new[]
        {
            times?.Count ?? 0,
            opens?.Count ?? 0,
            highs?.Count ?? 0,
            lows?.Count ?? 0,
            closes?.Count ?? 0,
            volumes?.Count ?? 0,
        }.Min();
        if (count == 0)
            return [];

        var result = new RupeezyCandle[count];
        for (var index = 0; index < count; index++)
        {
            result[index] = new()
            {
                Time = times[index].Value<long>().FromUnixSeconds(),
                Open = opens[index].Value<decimal>(),
                High = highs[index].Value<decimal>(),
                Low = lows[index].Value<decimal>(),
                Close = closes[index].Value<decimal>(),
                Volume = volumes[index].Value<decimal>(),
            };
        }
        return result;
    }

    internal static RupeezyInstrument ParseInstrument(
        IReadOnlyDictionary<string, string> values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        string get(string name)
            => values.TryGetValue(name, out var value) ? value?.Trim() : null;

        var token = get("token");
        var exchange = get("exchange");
        var symbol = get("symbol");
        if (token.IsEmpty() || exchange.IsEmpty() || symbol.IsEmpty())
            return null;

        exchange.ToBoardCode();
        var rawTick = get("tick").ToDecimal();
        var expiry = get("expiry_date").ToRupeezyTime();
        return new()
        {
            Token = token,
            Exchange = exchange.ToUpperInvariant(),
            Symbol = symbol,
            InstrumentName = get("instrument_name"),
            Series = get("series"),
            Expiry = expiry,
            OptionType = get("option_type"),
            StrikePrice = get("strike_price").ToDecimal(),
            TickSize = rawTick >= 1 ? rawTick / 100m : rawTick,
            LotSize = get("lot_size").ToDecimal() is > 0 and var lotSize
                ? lotSize
                : 1,
            SecurityDescription = get("security_desc").IsEmpty(symbol),
            LastTradingDate = get("last_trading_date").ToRupeezyTime(),
            Isin = get("isin_code"),
            Ticker = get("ticker"),
        };
    }

    private async Task<JToken> SendAuthenticated(
        HttpMethod method,
        string path,
        JToken body,
        CancellationToken cancellationToken)
    {
        AccessToken.ThrowIfEmpty(nameof(AccessToken));
        return await SendCore(method, path, body, true, cancellationToken);
    }

    private async Task<JToken> SendCore(
        HttpMethod method,
        string path,
        JToken body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");
        }
        request.Headers.TryAddWithoutValidation("x-api-key", _apiKey);
        if (authenticated)
            request.Headers.Authorization = new("Bearer", AccessToken);

        this.AddVerboseLog("Rupeezy {0} {1}.", method, path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            var data = ParseResponse(content, path);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Rupeezy {path} returned HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }
            return data;
        }
        catch (Exception ex) when (
            !response.IsSuccessStatusCode &&
            ex is InvalidDataException or InvalidOperationException)
        {
            throw new HttpRequestException(
                $"Rupeezy {path} returned HTTP {(int)response.StatusCode}: {ex.Message}",
                ex,
                response.StatusCode);
        }
    }

    private async Task<RupeezyInstrument[]> DownloadInstruments(
        CancellationToken cancellationToken)
    {
        this.AddVerboseLog("Rupeezy GET {0}.", _masterAddress);
        using var response = await _httpClient.GetAsync(
            _masterAddress,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var instruments = await ParseInstrumentCsv(stream, cancellationToken);
        if (instruments.Length == 0)
        {
            throw new InvalidDataException(
                "The Rupeezy master CSV did not contain any supported instruments.");
        }
        return instruments;
    }

    internal static async Task<RupeezyInstrument[]> ParseInstrumentCsv(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1 << 16, true);
        var csv = new FastCsvReader(reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        if (!await csv.NextLineAsync(cancellationToken))
            return [];

        var headers = new string[_masterColumnCount];
        for (var index = 0; index < headers.Length; index++)
            headers[index] = csv.ReadString()?.Trim().TrimStart('\uFEFF');

        var result = new List<RupeezyInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new Dictionary<string, string>(
                headers.Length,
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Length; index++)
                values[headers[index]] = csv.ReadString()?.Trim();

            try
            {
                var instrument = ParseInstrument(values);
                if (instrument != null)
                    result.Add(instrument);
            }
            catch (ArgumentOutOfRangeException)
            {
                // The public master may add a new exchange before the API exposes it.
            }
        }
        return [.. result];
    }
}
