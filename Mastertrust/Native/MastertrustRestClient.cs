namespace StockSharp.Mastertrust.Native;

sealed class MastertrustRestClient : BaseLogReceiver
{
    private const int _masterColumnCount = 14;
    private const long _maximumMasterSize = 128L * 1024 * 1024;

    private static readonly JsonSerializerSettings _jsonSettings = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };

    private readonly string _clientId;
    private readonly string _oauthClientId;
    private readonly string _oauthClientSecret;
    private readonly Uri _redirectUri;
    private readonly Uri _masterAddress;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _instrumentLock = new(1, 1);
    private MastertrustInstrument[] _instruments;
    private IReadOnlyDictionary<string, MastertrustInstrument> _instrumentsByKey;

    public MastertrustRestClient(
        string clientId,
        string oauthClientId,
        SecureString oauthClientSecret,
        SecureString accessToken,
        Uri redirectUri,
        Uri address,
        Uri masterAddress)
    {
        _clientId = clientId.ThrowIfEmpty(nameof(clientId));
        _oauthClientId = oauthClientId;
        _oauthClientSecret = oauthClientSecret?.UnSecure();
        AccessToken = accessToken?.UnSecure();
        _redirectUri = redirectUri;
        _masterAddress = masterAddress ??
            throw new ArgumentNullException(nameof(masterAddress));
        _httpClient = new()
        {
            BaseAddress = address ?? throw new ArgumentNullException(nameof(address)),
        };
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Mastertrust/1.0");
    }

    public override string Name =>
        nameof(Mastertrust) + "_" + nameof(MastertrustRestClient);

    public string AccessToken { get; private set; }

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        _instrumentLock.Dispose();
        base.DisposeManaged();
    }

    public async Task<MastertrustLoginResult> Authenticate(
        SecureString authorizationCode,
        CancellationToken cancellationToken)
    {
        if (!AccessToken.IsEmpty())
        {
            return new()
            {
                AccessToken = AccessToken,
            };
        }

        _oauthClientId.ThrowIfEmpty(nameof(_oauthClientId));
        _oauthClientSecret.ThrowIfEmpty(nameof(_oauthClientSecret));
        var code = authorizationCode
            .ThrowIfEmpty(nameof(authorizationCode))
            .UnSecure();
        _ = _redirectUri ??
            throw new ArgumentNullException(nameof(_redirectUri));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "oauth2/token");
        request.Headers.Authorization = new(
            "Basic",
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(
                    $"{_oauthClientId}:{_oauthClientSecret}")));
        request.Content = new FormUrlEncodedContent(
        [
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", _redirectUri.ToString()),
        ]);

        this.AddVerboseLog("Mastertrust POST oauth2/token.");
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var data = ParseHttpResponse(
            response,
            content,
            "oauth2/token");
        AccessToken = data.GetText("access_token", "token")
            .IsEmpty(data.GetValueIgnoreCase("data")
                ?.GetText("access_token", "token"))
            .ThrowIfEmpty(nameof(MastertrustLoginResult.AccessToken));
        return new()
        {
            AccessToken = AccessToken,
        };
    }

    public async Task<MastertrustProfile> GetProfile(
        CancellationToken cancellationToken)
        => (await SendAuthenticated(
            HttpMethod.Get,
            $"api/v1/user/profile?client_id={Escape(_clientId)}",
            null,
            cancellationToken)).ToObject<MastertrustProfile>(
                JsonSerializer.Create(_jsonSettings));

    public async Task<MastertrustInstrument[]> GetInstruments(
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
                    instrument => instrument.Exchange.ToInstrumentKey(
                        instrument.Token),
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

    public async Task<MastertrustInstrument> GetInstrument(
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
        MastertrustProducts product,
        OrderTypes orderType,
        decimal quantity,
        decimal price,
        decimal triggerPrice,
        decimal disclosedQuantity,
        decimal marketProtectionPercentage,
        TimeInForce? timeInForce,
        bool isAfterMarket,
        string userOrderId,
        CancellationToken cancellationToken)
    {
        var body = CreateOrderBody(
            exchange,
            token,
            side,
            product,
            orderType,
            quantity,
            price,
            triggerPrice,
            disclosedQuantity,
            marketProtectionPercentage,
            timeInForce,
            isAfterMarket);
        body["user_order_id"] = userOrderId;

        var data = await SendAuthenticated(
            HttpMethod.Post,
            "api/v1/orders",
            body,
            cancellationToken);
        return data.GetText("oms_order_id", "client_order_id")
            .ThrowIfEmpty(nameof(MastertrustOrder.OrderId));
    }

    public async Task<string> ModifyOrder(
        string orderId,
        string exchange,
        string token,
        Sides side,
        MastertrustProducts product,
        OrderTypes orderType,
        decimal quantity,
        decimal price,
        decimal triggerPrice,
        decimal disclosedQuantity,
        TimeInForce? timeInForce,
        CancellationToken cancellationToken)
    {
        var body = CreateOrderBody(
            exchange,
            token,
            side,
            product,
            orderType,
            quantity,
            price,
            triggerPrice,
            disclosedQuantity,
            0,
            timeInForce,
            false);
        body["oms_order_id"] = orderId.ThrowIfEmpty(nameof(orderId));
        body.Remove("market_protection_percentage");

        await SendAuthenticated(
            HttpMethod.Put,
            "api/v1/orders",
            body,
            cancellationToken);
        return orderId;
    }

    public async Task<string> CancelOrder(
        string orderId,
        CancellationToken cancellationToken)
    {
        await SendAuthenticated(
            HttpMethod.Delete,
            $"api/v1/orders/{Escape(orderId.ThrowIfEmpty(nameof(orderId)))}" +
            $"?client_id={Escape(_clientId)}",
            null,
            cancellationToken);
        return orderId;
    }

    public async Task<MastertrustOrder[]> GetOrders(
        CancellationToken cancellationToken)
    {
        var pendingTask = GetOrders("pending", cancellationToken);
        var completedTask = GetOrders("completed", cancellationToken);
        await Task.WhenAll(pendingTask, completedTask);
        return pendingTask.Result
            .Concat(completedTask.Result)
            .Where(order => order != null && !order.OrderId.IsEmpty())
            .GroupBy(order => order.OrderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    public async Task<MastertrustTrade[]> GetTrades(
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            $"api/v1/trades?client_id={Escape(_clientId)}",
            null,
            cancellationToken);
        return ParseArray<MastertrustTrade>(
            data.GetValueIgnoreCase("trades") ?? data);
    }

    public async Task<MastertrustPosition[]> GetPositions(
        CancellationToken cancellationToken)
        => ParseArray<MastertrustPosition>(
            await SendAuthenticated(
                HttpMethod.Get,
                $"api/v1/positions?type=live&client_id={Escape(_clientId)}",
                null,
                cancellationToken));

    public async Task<MastertrustHolding[]> GetHoldings(
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            $"api/v1/holdings?client_id={Escape(_clientId)}",
            null,
            cancellationToken);
        return ParseArray<MastertrustHolding>(
            data.GetValueIgnoreCase("holdings") ?? data);
    }

    public async Task<MastertrustFund> GetFunds(
        CancellationToken cancellationToken)
        => ParseFunds(await SendAuthenticated(
            HttpMethod.Get,
            $"api/v1/funds/view?client_id={Escape(_clientId)}&type=all",
            null,
            cancellationToken));

    internal static JToken ParseResponse(string content, string operation)
    {
        if (content.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Mastertrust returned an empty response for {operation}.");
        }

        JToken root;
        try
        {
            root = JToken.Parse(content);
        }
        catch (JsonReaderException ex)
        {
            throw new InvalidDataException(
                $"Mastertrust returned invalid JSON for {operation}.",
                ex);
        }

        if (root is not JObject obj)
            return root;

        if (obj.GetValueIgnoreCase("error") is JObject error)
        {
            var code = error.GetDecimal("code") ?? 0;
            var message = error.GetText("message");
            if (code != 0 || !message.IsEmpty())
            {
                throw new InvalidOperationException(
                    $"Mastertrust {operation} error: " +
                    message.IsEmpty(code.ToString(CultureInfo.InvariantCulture)));
            }
        }

        var status = obj.GetText("status");
        if (status.IsEmpty())
        {
            return obj.GetValueIgnoreCase("data", "result") ?? obj;
        }
        if (status.EqualsIgnoreCase("success") ||
            status.EqualsIgnoreCase("ok") ||
            status.EqualsIgnoreCase("true"))
        {
            return obj.GetValueIgnoreCase("data", "result") ?? obj;
        }

        var errorMessage = obj.GetText("message", "error_description", "detail")
            .IsEmpty(status)
            .IsEmpty("Unknown API error.");
        throw new InvalidOperationException(
            $"Mastertrust {operation} error: {errorMessage}");
    }

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

    internal static MastertrustFund ParseFunds(JToken data)
    {
        var result = new MastertrustFund();
        if (data is not JObject obj ||
            obj.GetValueIgnoreCase("values") is not JArray rows)
            return result;

        foreach (var row in rows.OfType<JArray>())
        {
            if (row.Count < 2)
                continue;
            var name = row[0]?.ToString()?.Trim();
            var value = row[1]?.ToString().ToDecimal() ?? 0;
            switch (name?.Replace(" ", string.Empty).ToUpperInvariant())
            {
                case "AVAILABLE":
                    result.Available = value;
                    break;
                case "MARGINUSED":
                    result.MarginUsed = value;
                    break;
                case "CASHMARGIN":
                    result.CashMargin = value;
                    break;
                case "COLLATERAL":
                    result.Collateral = value;
                    break;
                case "PAYIN":
                    result.PayIn = value;
                    break;
                case "PAYOUT":
                    result.PayOut = value;
                    break;
            }
        }
        return result;
    }

    internal static MastertrustInstrument ParseInstrument(
        IReadOnlyDictionary<string, string> values)
    {
        if (values == null)
            throw new ArgumentNullException(nameof(values));

        string get(string name)
            => values.TryGetValue(name, out var value) ? value?.Trim() : null;

        var token = get("exchange_token");
        var exchange = get("exchange");
        var symbol = get("trading_symbol");
        if (token.IsEmpty() || exchange.IsEmpty() || symbol.IsEmpty())
            return null;

        exchange.ToBoardCode();
        var lotSize = get("lot_size").ToDecimal();
        return new()
        {
            Token = token,
            Exchange = exchange.ToUpperInvariant(),
            TradingSymbol = symbol,
            CompanyName = get("company_name").IsEmpty(symbol),
            ClosePrice = get("close_price").ToDecimal(),
            Expiry = get("expiry").ToMastertrustTime(),
            Strike = get("strike").ToDecimal(),
            TickSize = get("tick_size").ToDecimal(),
            LotSize = lotSize > 0 ? lotSize : 1,
            InstrumentName = get("instrument_name"),
            OptionType = get("option_type"),
            Segment = get("segment"),
            FinancialProductCode = get("fin_instrm_pdct_tp_cd"),
            AssetCode = get("asset_code"),
        };
    }

    internal static async Task<MastertrustInstrument[]> ParseInstrumentArchive(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Read,
            true);
        var entry = archive.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "The Mastertrust instrument archive does not contain a CSV file.");
        if (entry.Length <= 0 || entry.Length > _maximumMasterSize)
        {
            throw new InvalidDataException(
                $"The Mastertrust instrument CSV size {entry.Length} is invalid.");
        }

        await using var entryStream = entry.Open();
        return await ParseInstrumentCsv(entryStream, cancellationToken);
    }

    internal static async Task<MastertrustInstrument[]> ParseInstrumentCsv(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));

        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            true,
            1 << 16,
            true);
        var csv = new FastCsvReader(reader, StringHelper.N)
        {
            ColumnSeparator = ',',
        };
        if (!await csv.NextLineAsync(cancellationToken))
            return [];

        var headers = new string[_masterColumnCount];
        for (var index = 0; index < headers.Length; index++)
            headers[index] = csv.ReadString()?.Trim().TrimStart('\uFEFF');
        if (!headers.Contains("exchange_token", StringComparer.OrdinalIgnoreCase) ||
            !headers.Contains("exchange", StringComparer.OrdinalIgnoreCase) ||
            !headers.Contains("trading_symbol", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Mastertrust instrument CSV has an unsupported header.");
        }

        var result = new List<MastertrustInstrument>();
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
                // A new venue can appear in the public master before its
                // streaming numeric code is published.
            }
        }
        return [.. result];
    }

    private async Task<MastertrustOrder[]> GetOrders(
        string type,
        CancellationToken cancellationToken)
    {
        var data = await SendAuthenticated(
            HttpMethod.Get,
            $"api/v1/orders?type={Escape(type)}&client_id={Escape(_clientId)}",
            null,
            cancellationToken);
        return ParseArray<MastertrustOrder>(
            data.GetValueIgnoreCase("orders") ?? data);
    }

    private JObject CreateOrderBody(
        string exchange,
        string token,
        Sides side,
        MastertrustProducts product,
        OrderTypes orderType,
        decimal quantity,
        decimal price,
        decimal triggerPrice,
        decimal disclosedQuantity,
        decimal marketProtectionPercentage,
        TimeInForce? timeInForce,
        bool isAfterMarket)
    {
        var body = new JObject
        {
            ["client_id"] = _clientId,
            ["disclosed_quantity"] = disclosedQuantity,
            ["exchange"] = exchange,
            ["instrument_token"] = token,
            ["market_protection_percentage"] = marketProtectionPercentage,
            ["order_side"] = side.ToNative(),
            ["order_type"] = orderType.ToNative(price),
            ["price"] = orderType == OrderTypes.Market ? 0 : price,
            ["product"] = product.ToNative(),
            ["quantity"] = quantity,
            ["trigger_price"] = triggerPrice,
            ["validity"] = timeInForce.ToValidity(),
        };
        if (isAfterMarket)
            body["amo"] = true;
        return body;
    }

    private async Task<JToken> SendAuthenticated(
        HttpMethod method,
        string path,
        JToken body,
        CancellationToken cancellationToken)
    {
        AccessToken.ThrowIfEmpty(nameof(AccessToken));
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", AccessToken);
        if (body != null)
        {
            request.Content = new StringContent(
                body.ToString(Formatting.None),
                Encoding.UTF8,
                "application/json");
        }

        this.AddVerboseLog("Mastertrust {0} {1}.", method, path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseHttpResponse(response, content, path);
    }

    private static JToken ParseHttpResponse(
        HttpResponseMessage response,
        string content,
        string operation)
    {
        try
        {
            var data = ParseResponse(content, operation);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Mastertrust {operation} returned HTTP {(int)response.StatusCode}.",
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
                $"Mastertrust {operation} returned HTTP {(int)response.StatusCode}: {ex.Message}",
                ex,
                response.StatusCode);
        }
    }

    private async Task<MastertrustInstrument[]> DownloadInstruments(
        CancellationToken cancellationToken)
    {
        this.AddVerboseLog("Mastertrust GET {0}.", _masterAddress);
        using var response = await _httpClient.GetAsync(
            _masterAddress,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (bytes.Length == 0)
        {
            throw new InvalidDataException(
                "The Mastertrust instrument archive is empty.");
        }

        using var stream = new MemoryStream(bytes, false);
        var instruments = await ParseInstrumentArchive(
            stream,
            cancellationToken);
        if (instruments.Length == 0)
        {
            throw new InvalidDataException(
                "The Mastertrust instrument archive did not contain any supported instruments.");
        }
        return instruments;
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));
}
