namespace StockSharp.PaytmMoney.Native;

sealed class PaytmMoneyRestClient : BaseLogReceiver
{
    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Culture = CultureInfo.InvariantCulture,
    };

    private readonly HttpClient _http;
    private readonly Uri _address;
    private readonly string _securityMasterFile;
    private readonly int _maxAttempts;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private string _accessToken;
    private string _readAccessToken;
    private PaytmMoneyInstrument[] _instruments;
    private IReadOnlyDictionary<string, PaytmMoneyInstrument> _instrumentsByKey;
    private IReadOnlyDictionary<string, PaytmMoneyInstrument> _instrumentsByIsin;
    private IReadOnlyDictionary<string, PaytmMoneyInstrument> _instrumentsByExchangeAndId;

    public PaytmMoneyRestClient(
        Uri address,
        string securityMasterFile,
        string accessToken,
        string readAccessToken,
        int maxAttempts)
    {
        _address = EnsureTrailingSlash(
            address ?? throw new ArgumentNullException(nameof(address)));
        _securityMasterFile = securityMasterFile
            .ThrowIfEmpty(nameof(securityMasterFile));
        _maxAttempts = Math.Max(1, maxAttempts);
        _accessToken = NormalizeToken(accessToken);
        _readAccessToken = NormalizeToken(readAccessToken);

        _http = new(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            BaseAddress = _address,
            Timeout = TimeSpan.FromSeconds(45),
        };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-PaytmMoney-Connector/1.0");
    }

    public override string Name => "PaytmMoney_Rest";

    protected override void DisposeManaged()
    {
        _instrumentLock.Dispose();
        _http.Dispose();
        base.DisposeManaged();
    }

    public void SetTokens(string accessToken, string readAccessToken)
    {
        _accessToken = NormalizeToken(accessToken);
        _readAccessToken = NormalizeToken(readAccessToken);
    }

    public async Task<PaytmMoneyTokens> GenerateSession(
        string apiKey,
        string apiSecret,
        string requestToken,
        CancellationToken cancellationToken)
    {
        var content = await SendRaw(
            HttpMethod.Post,
            "accounts/v2/gettoken",
            new
            {
                api_key = apiKey.ThrowIfEmpty(nameof(apiKey)),
                api_secret_key = apiSecret.ThrowIfEmpty(nameof(apiSecret)),
                request_token = requestToken.ThrowIfEmpty(nameof(requestToken)),
            },
            null,
            cancellationToken);
        var tokens = Extract<PaytmMoneyTokens>(content, "session");
        if (tokens.AccessToken.IsEmpty() &&
            tokens.ReadAccessToken.IsEmpty() &&
            tokens.PublicAccessToken.IsEmpty())
        {
            throw new InvalidOperationException(
                "Paytm Money did not return session tokens.");
        }

        SetTokens(tokens.AccessToken, tokens.ReadAccessToken);
        return tokens;
    }

    public async Task<PaytmMoneyUser> GetUser(
        CancellationToken cancellationToken)
        => Extract<PaytmMoneyUser>(
            await SendRaw(
                HttpMethod.Get,
                "accounts/v1/user/details",
                null,
                ReadToken,
                cancellationToken),
            "user details");

    public async Task<PaytmMoneyInstrument[]> GetInstruments(
        CancellationToken cancellationToken)
    {
        if (_instruments != null)
            return _instruments;

        await _instrumentLock.WaitAsync(cancellationToken);
        try
        {
            if (_instruments != null)
                return _instruments;

            using var response = await SendRequest(
                HttpMethod.Get,
                $"data/v1/scrips/{Uri.EscapeDataString(_securityMasterFile)}",
                null,
                null,
                cancellationToken,
                acceptCsv: true);
            var content = await response.Content
                .ReadAsStringAsync(cancellationToken);
            _instruments = await ParseInstruments(
                content, cancellationToken);
            _instrumentsByKey = _instruments
                .GroupBy(
                    instrument => instrument.ToInstrumentKey(),
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _instrumentsByIsin = _instruments
                .Where(instrument => !instrument.Isin.IsEmpty())
                .GroupBy(
                    instrument => instrument.Isin,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
            _instrumentsByExchangeAndId = _instruments
                .GroupBy(
                    instrument => CreateExchangeIdKey(
                        instrument.Exchange, instrument.SecurityId),
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

    public async Task<PaytmMoneyInstrument> GetInstrument(
        string instrumentKey,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        return _instrumentsByKey.TryGetValue(
            instrumentKey, out var instrument)
                ? instrument
                : null;
    }

    public async Task<PaytmMoneyInstrument> FindInstrument(
        string exchange,
        string securityId,
        string isin,
        CancellationToken cancellationToken)
    {
        await GetInstruments(cancellationToken);
        if (!isin.IsEmpty() &&
            _instrumentsByIsin.TryGetValue(isin, out var instrument))
        {
            return instrument;
        }

        return _instrumentsByExchangeAndId.TryGetValue(
            CreateExchangeIdKey(exchange, securityId),
            out instrument)
                ? instrument
                : null;
    }

    public async Task<PaytmMoneyLiveTick[]> GetLive(
        string mode,
        IEnumerable<PaytmMoneyInstrument> instruments,
        CancellationToken cancellationToken)
    {
        var preferences = instruments
            .Where(instrument => instrument != null)
            .Select(instrument =>
                $"{instrument.Exchange}:{instrument.SecurityId}:{instrument.ScripType}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (preferences.Length == 0)
            return [];

        var path =
            $"data/v1/price/live?mode={Uri.EscapeDataString(mode.ThrowIfEmpty(nameof(mode)))}" +
            $"&pref={Uri.EscapeDataString(string.Join(',', preferences))}";
        var response = JsonConvert.DeserializeObject<PaytmMoneyLiveResponse>(
            await SendRaw(
                HttpMethod.Get, path, null, ReadToken,
                cancellationToken),
            _jsonSettings);
        return response?.Data ?? [];
    }

    public async Task<PaytmMoneyCandle[]> GetCandles(
        PaytmMoneyInstrument instrument,
        TimeSpan timeFrame,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrument);
        var interval = timeFrame switch
        {
            var value when value == TimeSpan.FromMinutes(1) => "MINUTE",
            var value when value == TimeSpan.FromDays(1) => "DAY",
            _ => throw new ArgumentOutOfRangeException(
                nameof(timeFrame), timeFrame,
                "Paytm Money history supports one-minute and daily candles."),
        };
        var end = (to ?? DateTime.UtcNow).ToIndiaTime();
        var start = (from ?? end.AddDays(
            interval == "DAY" ? -3650 : -30)).ToIndiaTime();
        if (start > end)
            return [];

        var body = new JObject
        {
            ["cont"] = false,
            ["exchange"] = instrument.Exchange,
            ["expiry"] = instrument.ExpiryDate?.ToString(
                "yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["fromDate"] = start.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["instType"] = instrument.HistoryType.IsEmpty(
                instrument.ScripType),
            ["interval"] = interval,
            ["series"] = instrument.Series,
            ["strike"] = instrument.StrikePrice,
            ["symbol"] = instrument.Symbol,
            ["toDate"] = end.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        };

        var root = ParseAndValidate(
            await SendRaw(
                HttpMethod.Post,
                "data/v1/price-charts/sym",
                body,
                ReadToken,
                cancellationToken),
            "price history");
        var data = FindPayload(root);
        return ParseCandles(data);
    }

    public async Task<PaytmMoneyOrder[]> GetOrders(
        CancellationToken cancellationToken)
        => ExtractArray<PaytmMoneyOrder>(
            await SendRaw(
                HttpMethod.Get,
                "orders/v1/user/orders",
                null,
                ReadToken,
                cancellationToken),
            "orders");

    public async Task<PaytmMoneyTrade[]> GetTradeDetails(
        string orderNumber,
        string legNumber,
        string segment,
        CancellationToken cancellationToken)
    {
        var path =
            $"orders/v1/trade-details?order_no={Uri.EscapeDataString(orderNumber.ThrowIfEmpty(nameof(orderNumber)))}" +
            $"&leg_no={Uri.EscapeDataString(legNumber.IsEmpty("1"))}" +
            $"&segment={Uri.EscapeDataString(segment.ThrowIfEmpty(nameof(segment)))}";
        return ExtractArray<PaytmMoneyTrade>(
            await SendRaw(
                HttpMethod.Get, path, null, ReadToken,
                cancellationToken),
            "trade details");
    }

    public async Task<PaytmMoneyPosition[]> GetPositions(
        CancellationToken cancellationToken)
        => ExtractArray<PaytmMoneyPosition>(
            await SendRaw(
                HttpMethod.Get,
                "orders/v1/position",
                null,
                ReadToken,
                cancellationToken),
            "positions");

    public async Task<PaytmMoneyFunds> GetFunds(
        CancellationToken cancellationToken)
    {
        var root = ParseAndValidate(
            await SendRaw(
                HttpMethod.Get,
                "accounts/v1/funds/summary?config=true",
                null,
                ReadToken,
                cancellationToken),
            "funds");
        var payload = FindPayload(root);
        return payload?["funds_summary"]?.ToObject<PaytmMoneyFunds>(
                JsonSerializer.Create(_jsonSettings))
            ?? payload?.ToObject<PaytmMoneyFunds>(
                JsonSerializer.Create(_jsonSettings));
    }

    public async Task<PaytmMoneyHolding[]> GetHoldings(
        CancellationToken cancellationToken)
        => ExtractArray<PaytmMoneyHolding>(
            await SendRaw(
                HttpMethod.Get,
                "holdings/v1/get-user-holdings-data",
                null,
                ReadToken,
                cancellationToken),
            "holdings");

    public Task<PaytmMoneyOrderResult> PlaceOrder(
        PaytmMoneyOrderRequest request,
        PaytmMoneyProducts product,
        CancellationToken cancellationToken)
        => SendOrder(
            GetOrderPath("place", product),
            request,
            cancellationToken);

    public Task<PaytmMoneyOrderResult> ModifyOrder(
        PaytmMoneyOrderRequest request,
        PaytmMoneyProducts product,
        CancellationToken cancellationToken)
        => SendOrder(
            GetOrderPath("modify", product),
            request,
            cancellationToken);

    public Task<PaytmMoneyOrderResult> CancelOrder(
        PaytmMoneyOrderRequest request,
        PaytmMoneyProducts product,
        CancellationToken cancellationToken)
        => SendOrder(
            GetOrderPath("cancel", product),
            request,
            cancellationToken);

    private async Task<PaytmMoneyOrderResult> SendOrder(
        string path,
        PaytmMoneyOrderRequest request,
        CancellationToken cancellationToken)
    {
        var response = JsonConvert.DeserializeObject<PaytmMoneyOrderResponse>(
            await SendRaw(
                HttpMethod.Post,
                path,
                request ?? throw new ArgumentNullException(nameof(request)),
                _accessToken.ThrowIfEmpty("Access token"),
                cancellationToken),
            _jsonSettings)
            ?? throw new InvalidOperationException(
                $"Paytm Money returned an empty response for {path}.");
        if (response.Status?.EqualsIgnoreCase("success") != true &&
            response.Status?.EqualsIgnoreCase("ok") != true)
        {
            throw new InvalidOperationException(
                $"Paytm Money {path} error: " +
                response.Message.IsEmpty(response.ErrorCode));
        }

        var result = response.Data?.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Paytm Money did not return an order result for {path}.");
        if (!result.OmsErrorCode.IsEmpty() &&
            result.OrderNumber.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Paytm Money OMS error {result.OmsErrorCode}: " +
                response.Message);
        }
        return result;
    }

    private string ReadToken
        => !_readAccessToken.IsEmpty()
            ? _readAccessToken
            : _accessToken.ThrowIfEmpty("Read or access token");

    private async Task<string> SendRaw(
        HttpMethod method,
        string path,
        object body,
        string token,
        CancellationToken cancellationToken)
    {
        using var response = await SendRequest(
            method, path, body, token, cancellationToken);
        var content = await response.Content
            .ReadAsStringAsync(cancellationToken);
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Paytm Money returned an empty response for {path}.");
        }
        return content;
    }

    private async Task<HttpResponseMessage> SendRequest(
        HttpMethod method,
        string path,
        object body,
        string token,
        CancellationToken cancellationToken,
        bool acceptCsv = false)
    {
        path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation(
                "openapi-client-src", "sdk");
            if (!token.IsEmpty())
            {
                request.Headers.TryAddWithoutValidation(
                    "x-jwt-token", token);
            }
            if (acceptCsv)
            {
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("text/csv"));
            }
            if (body != null)
            {
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(
                        body, Formatting.None, _jsonSettings),
                    Encoding.UTF8,
                    "application/json");
            }

            this.AddVerboseLog(
                "Paytm Money {0} {1}.", method.Method, path);
            var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;

            var retry = attempt < _maxAttempts &&
                (response.StatusCode == HttpStatusCode.TooManyRequests ||
                    (int)response.StatusCode >= 500);
            if (retry)
            {
                response.Dispose();
                await Task.Delay(
                    TimeSpan.FromMilliseconds(250 * attempt),
                    cancellationToken);
                continue;
            }

            var content = await response.Content
                .ReadAsStringAsync(cancellationToken);
            var status = response.StatusCode;
            var reason = response.ReasonPhrase;
            response.Dispose();
            throw new HttpRequestException(
                $"Paytm Money {path} returned HTTP {(int)status} " +
                $"{reason}: {Truncate(content, 600)}");
        }
    }

    internal static string GetOrderPath(
        string action,
        PaytmMoneyProducts product)
    {
        action = action?.ToLowerInvariant();
        if (action is not ("place" or "modify" or "cancel"))
        {
            throw new ArgumentOutOfRangeException(
                nameof(action), action, null);
        }

        var kind = product switch
        {
            PaytmMoneyProducts.Cover => "cover",
            PaytmMoneyProducts.Bracket => "bracket",
            _ => "regular",
        };
        if (action == "cancel" && kind is "cover" or "bracket")
            action = "exit";
        return $"orders/v1/{action}/{kind}";
    }

    internal static async Task<PaytmMoneyInstrument[]>
        ParseInstruments(
            string content,
            CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty())
            return [];

        using var reader = new StringReader(content);
        var csv = new FastCsvReader(reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        if (!await csv.NextLineAsync(cancellationToken))
            return [];

        var headers = new string[csv.ColumnCount];
        for (var index = 0; index < headers.Length; index++)
            headers[index] = NormalizeHeader(csv.ReadString());

        var instruments = new List<PaytmMoneyInstrument>();
        while (await csv.NextLineAsync(cancellationToken))
        {
            var values = new string[csv.ColumnCount];
            for (var index = 0; index < values.Length; index++)
                values[index] = csv.ReadString()?.Trim();

            string Get(params string[] names)
            {
                foreach (var name in names)
                {
                    var index = Array.IndexOf(
                        headers, NormalizeHeader(name));
                    if (index >= 0 && index < values.Length &&
                        !values[index].IsEmpty())
                    {
                        return NullIfNotAvailable(values[index]);
                    }
                }
                return null;
            }

            var securityId = Get(
                "security_id", "securityid", "scrip_id",
                "scripid", "token");
            var exchange = Get(
                "exchange", "exchange_type", "exch")
                ?.ToUpperInvariant();
            if (securityId.IsEmpty() || exchange.IsEmpty())
                continue;

            var rawType = Get(
                "scrip_type", "instrument_type", "instrument",
                "inst_type", "type");
            var historyType = Get(
                "history_type", "historical_instrument_type",
                "inst_type", "instrument_type");
            var scripType = InferScripType(rawType, historyType);
            historyType = InferHistoryType(
                historyType, rawType, scripType,
                Get("symbol", "trading_symbol", "tradingsymbol"));
            var segment = Get(
                "segment", "segment_type", "market_segment");
            segment = NormalizeSegment(segment, scripType);

            try
            {
                exchange.ToBoardCode(segment, scripType);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var symbol = Get(
                "trading_symbol", "tradingsymbol", "symbol",
                "exchange_symbol", "display_symbol");
            instruments.Add(new()
            {
                SecurityId = securityId,
                Exchange = exchange,
                Segment = segment,
                ScripType = scripType,
                HistoryType = historyType,
                Symbol = symbol.IsEmpty(securityId),
                Name = Get(
                    "name", "display_name", "company_name",
                    "description").IsEmpty(symbol),
                Isin = Get("isin", "isin_code"),
                Series = Get("series"),
                UnderlyingSymbol = Get(
                    "underlying_symbol", "underlying"),
                TickSize = ParseDecimal(
                    Get("tick_size", "ticksize")),
                LotSize = ParseDecimal(
                    Get("lot_size", "lotsize", "market_lot")),
                ExpiryDate = ParseDate(
                    Get("expiry", "expiry_date", "expirydate")),
                StrikePrice = ParseDecimal(
                    Get("strike", "strike_price", "strikeprice")),
                OptionType = Get(
                    "option_type", "opt_type", "optiontype"),
            });
        }

        return [.. instruments];
    }

    internal static PaytmMoneyCandle[] ParseCandles(JToken token)
    {
        token = FindPayload(token);
        if (token is JObject obj)
        {
            token = obj["candles"] ?? obj["results"] ??
                obj["data"] ?? obj["values"];
        }
        if (token is not JArray rows)
            return [];

        var result = new List<PaytmMoneyCandle>();
        foreach (var row in rows.OfType<JArray>())
        {
            if (row.Count < 6)
                continue;
            var timestamp = ParseCandleTime(row[0]);
            var open = ToDecimal(row[1]);
            var high = ToDecimal(row[2]);
            var low = ToDecimal(row[3]);
            var close = ToDecimal(row[4]);
            if (timestamp == null ||
                open == null || high == null ||
                low == null || close == null)
            {
                continue;
            }

            result.Add(new()
            {
                Time = timestamp.Value,
                Open = open.Value,
                High = high.Value,
                Low = low.Value,
                Close = close.Value,
                Volume = ToDecimal(row.ElementAtOrDefault(5)) ?? 0,
                OpenInterest = ToDecimal(row.ElementAtOrDefault(6)),
            });
        }
        return [.. result
            .GroupBy(candle => candle.Time)
            .Select(group => group.Last())
            .OrderBy(candle => candle.Time)];
    }

    private static T Extract<T>(string content, string operation)
        where T : class
    {
        var payload = FindPayload(
            ParseAndValidate(content, operation));
        return payload?.ToObject<T>(
                JsonSerializer.Create(_jsonSettings))
            ?? throw new InvalidOperationException(
                $"Paytm Money returned no {operation} data.");
    }

    private static T[] ExtractArray<T>(
        string content, string operation)
        where T : class
    {
        var payload = FindPayload(
            ParseAndValidate(content, operation));
        if (payload is JObject obj)
        {
            payload = obj["results"] ?? obj["orders"] ??
                obj["positions"] ?? obj["holdings"] ??
                obj["trades"] ?? obj["data"];
        }
        if (payload == null || payload.Type == JTokenType.Null)
            return [];
        if (payload is not JArray)
            payload = new JArray(payload);
        return payload.ToObject<T[]>(
            JsonSerializer.Create(_jsonSettings)) ?? [];
    }

    private static JToken ParseAndValidate(
        string content, string operation)
    {
        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Paytm Money returned invalid JSON for {operation}.",
                error);
        }

        if (root is JObject obj)
        {
            var status = (string)obj["status"];
            var errorCode = (string)obj["error_code"];
            var meta = obj["meta"] as JObject;
            var metaCode = (string)meta?["code"];
            var isFailure =
                status?.EqualsIgnoreCase("failed") == true ||
                status?.EqualsIgnoreCase("failure") == true ||
                status?.EqualsIgnoreCase("error") == true ||
                (!errorCode.IsEmpty() &&
                    status?.EqualsIgnoreCase("success") != true) ||
                (!metaCode.IsEmpty() &&
                    metaCode is not ("200" or "201" or "0"));
            if (isFailure)
            {
                var message =
                    (string)obj["message"] ??
                    (string)meta?["displayMessage"] ??
                    (string)meta?["message"] ??
                    errorCode ?? metaCode;
                throw new InvalidOperationException(
                    $"Paytm Money {operation} error: {message}");
            }
        }
        return root;
    }

    private static JToken FindPayload(JToken token)
    {
        if (token is not JObject obj)
            return token;

        var payload = obj["data"] ?? obj["results"];
        if (payload is JObject data)
        {
            return data["results"] ?? data["data"] ?? payload;
        }
        return payload ?? token;
    }

    private static string InferScripType(
        string rawType, string historyType)
    {
        var value = $"{rawType} {historyType}".ToUpperInvariant();
        if (value.Contains("ETF"))
            return "ETF";
        if (value.Contains("INDEX") || value.Trim() == "I")
            return "INDEX";
        if (value.Contains("OPT"))
            return "OPTION";
        if (value.Contains("FUT"))
            return "FUTURE";
        return "EQUITY";
    }

    private static string InferHistoryType(
        string historyType,
        string rawType,
        string scripType,
        string symbol)
    {
        var value = historyType.IsEmpty(rawType)?.ToUpperInvariant();
        if (value is "ES" or "I" or "ETF" or
            "FUTIDX" or "FUTSTK" or "OPTIDX" or "OPTSTK")
        {
            return value;
        }
        return scripType switch
        {
            "INDEX" => "I",
            "ETF" => "ETF",
            "FUTURE" when IsIndexSymbol(symbol) => "FUTIDX",
            "FUTURE" => "FUTSTK",
            "OPTION" when IsIndexSymbol(symbol) => "OPTIDX",
            "OPTION" => "OPTSTK",
            _ => "ES",
        };
    }

    private static bool IsIndexSymbol(string symbol)
    {
        symbol = symbol?.ToUpperInvariant();
        return symbol?.Contains("NIFTY") == true ||
            symbol?.Contains("SENSEX") == true ||
            symbol?.Contains("BANKEX") == true;
    }

    private static string NormalizeSegment(
        string segment, string scripType)
    {
        segment = segment?.Trim().ToUpperInvariant();
        if (segment is "E" or "D" or "I")
            return segment;
        if (segment is "EQ" or "CASH" or "EQUITY")
            return "E";
        if (segment is "FO" or "FNO" or "DERIVATIVE" or
            "DERIVATIVES")
        {
            return "D";
        }
        return scripType switch
        {
            "INDEX" => "I",
            "FUTURE" or "OPTION" => "D",
            _ => "E",
        };
    }

    private static string NormalizeHeader(string value)
        => new((value ?? string.Empty)
            .Trim('\uFEFF', ' ', '"')
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string NullIfNotAvailable(string value)
        => value.IsEmpty() ||
            value.EqualsIgnoreCase("NA") ||
            value.EqualsIgnoreCase("N/A") ||
            value.EqualsIgnoreCase("NULL")
                ? null
                : value;

    private static decimal? ParseDecimal(string value)
        => decimal.TryParse(
            value,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out var result)
                ? result
                : null;

    private static DateTime? ParseDate(string value)
    {
        if (value.IsEmpty())
            return null;
        if (long.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var epoch))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(epoch)
                    .UtcDateTime.Date;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var date)
                ? DateTime.SpecifyKind(date.Date, DateTimeKind.Utc)
                : null;
    }

    private static DateTime? ParseCandleTime(JToken token)
    {
        if (token == null)
            return null;
        if (token.Type is JTokenType.Integer or JTokenType.Float &&
            long.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var epoch))
        {
            try
            {
                if (epoch > 10_000_000_000)
                    return DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                        .UtcDateTime;
                return DateTimeOffset.FromUnixTimeSeconds(epoch)
                    .UtcDateTime;
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return token.ToString().ToPaytmTime();
    }

    private static decimal? ToDecimal(JToken token)
        => token == null || token.Type == JTokenType.Null
            ? null
            : decimal.TryParse(
                token.ToString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var value)
                    ? value
                    : null;

    private static string NormalizeToken(string token)
    {
        token = token?.Trim();
        return token?.StartsWith(
            "Bearer ", StringComparison.OrdinalIgnoreCase) == true
                ? token[7..].Trim()
                : token;
    }

    private static string CreateExchangeIdKey(
        string exchange, string securityId)
        => $"{exchange?.ToUpperInvariant()}|{securityId}";

    private static Uri EnsureTrailingSlash(Uri address)
        => address.AbsoluteUri.EndsWith(
            "/", StringComparison.Ordinal)
                ? address
                : new Uri(address.AbsoluteUri + "/");

    private static string Truncate(string value, int length)
        => value.IsEmpty() || value.Length <= length
            ? value
            : value[..length] + "...";
}
