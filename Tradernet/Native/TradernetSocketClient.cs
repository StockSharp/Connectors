namespace StockSharp.Tradernet.Native;

sealed class TradernetSocketClient : BaseLogReceiver
{
    private readonly WebSocketClient _client;
    private readonly SynchronizedSet<string> _quoteTickers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SynchronizedSet<string> _bookTickers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        ContractResolver =
            new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
    };
    private bool _portfolio;
    private bool _orders;

    public TradernetSocketClient(Uri address, string sid,
        int reconnectAttempts, WorkingTime workingTime)
    {
        if (address is null || !address.IsAbsoluteUri ||
            address.Scheme is not ("ws" or "wss"))
        {
            throw new ArgumentException(
                "A valid Tradernet WebSocket address is required.",
                nameof(address));
        }

        _client = new(
            BuildAddress(address, sid),
            (state, token) => StateChanged is { } handler
                ? handler(state, token) : default,
            (error, token) => Error is { } handler
                ? handler(error, token) : default,
            Process,
            (format, args) => this.AddInfoLog(format, args),
            (format, args) => this.AddErrorLog(format, args),
            (format, args) => this.AddVerboseLog(format, args))
        {
            ReconnectAttempts = Math.Max(1, reconnectAttempts),
            WorkingTime = workingTime,
            DisableAutoResend = true,
        };
        _client.PostConnect += RestoreSubscriptions;
    }

    public override string Name => "Tradernet_WebSocket";

    public event Func<TradernetQuote,
        CancellationToken, ValueTask> QuoteReceived;
    public event Func<TradernetBookBlock,
        CancellationToken, ValueTask> BookReceived;
    public event Func<TradernetPortfolio,
        CancellationToken, ValueTask> PortfolioReceived;
    public event Func<TradernetOrder[],
        CancellationToken, ValueTask> OrdersReceived;
    public event Func<Exception,
        CancellationToken, ValueTask> Error;
    public event Func<ConnectionStates,
        CancellationToken, ValueTask> StateChanged;

    public ValueTask ConnectAsync(
        CancellationToken cancellationToken)
        => _client.ConnectAsync(cancellationToken);

    public ValueTask DisconnectAsync(
        CancellationToken cancellationToken)
        => _client.DisconnectAsync(cancellationToken);

    public ValueTask Ping(
        CancellationToken cancellationToken)
    {
        _client.SendOpCode(0x9);
        return default;
    }

    public async ValueTask SubscribeQuotes(
        string ticker, bool subscribe,
        CancellationToken cancellationToken)
    {
        ticker = NormalizeTicker(ticker);
        if (subscribe)
            _quoteTickers.Add(ticker);
        else
            _quoteTickers.Remove(ticker);
        await SendTickerList(
            "quotes", _quoteTickers,
            cancellationToken);
    }

    public async ValueTask SubscribeBook(
        string ticker, bool subscribe,
        CancellationToken cancellationToken)
    {
        ticker = NormalizeTicker(ticker);
        if (subscribe)
            _bookTickers.Add(ticker);
        else
            _bookTickers.Remove(ticker);
        await SendTickerList(
            "orderBook", _bookTickers,
            cancellationToken);
    }

    public async ValueTask SubscribePortfolio(
        CancellationToken cancellationToken)
    {
        if (_portfolio)
            return;
        _portfolio = true;
        await SendEvent("portfolio", cancellationToken);
    }

    public async ValueTask SubscribeOrders(
        CancellationToken cancellationToken)
    {
        if (_orders)
            return;
        _orders = true;
        await SendEvent("orders", cancellationToken);
    }

    private async ValueTask RestoreSubscriptions(
        bool isReconnect,
        CancellationToken cancellationToken)
    {
        if (_quoteTickers.Count > 0)
        {
            await SendTickerList(
                "quotes", _quoteTickers,
                cancellationToken);
        }
        if (_bookTickers.Count > 0)
        {
            await SendTickerList(
                "orderBook", _bookTickers,
                cancellationToken);
        }
        if (_portfolio)
            await SendEvent("portfolio", cancellationToken);
        if (_orders)
            await SendEvent("orders", cancellationToken);
    }

    private async ValueTask Process(
        WebSocketMessage message,
        CancellationToken cancellationToken)
    {
        var raw = message.AsString();
        if (raw.IsEmpty())
            return;

        JArray envelope;
        try
        {
            envelope = JArray.Parse(raw);
        }
        catch (JsonException error)
        {
            await RaiseError(new InvalidDataException(
                "Tradernet returned an invalid WebSocket message.",
                error), cancellationToken);
            return;
        }
        if (envelope.Count == 0)
            return;

        var eventName = envelope[0].Value<string>();
        var data = envelope.Count > 1
            ? envelope[1] : null;
        var serializer =
            JsonSerializer.Create(_jsonSettings);
        try
        {
            switch (eventName)
            {
                case "q":
                    if (QuoteReceived is { } quoteHandler)
                    {
                        foreach (var token in AsMany(data))
                        {
                            var quote =
                                token.ToObject<TradernetQuote>(
                                    serializer);
                            if (quote is not null)
                            {
                                await quoteHandler(
                                    quote,
                                    cancellationToken);
                            }
                        }
                    }
                    break;
                case "b":
                    if (BookReceived is { } bookHandler)
                    {
                        foreach (var token in AsMany(data))
                        {
                            var book =
                                token.ToObject<TradernetBookBlock>(
                                    serializer);
                            if (book is not null)
                            {
                                await bookHandler(
                                    book,
                                    cancellationToken);
                            }
                        }
                    }
                    break;
                case "portfolio":
                    if (PortfolioReceived is { } portfolioHandler)
                    {
                        var portfolio =
                            data?.ToObject<TradernetPortfolio>(
                                serializer);
                        if (portfolio is not null)
                        {
                            await portfolioHandler(
                                portfolio,
                                cancellationToken);
                        }
                    }
                    break;
                case "orders":
                    if (OrdersReceived is { } ordersHandler)
                    {
                        var token = data is JObject obj
                            ? obj["orders"] ??
                                obj["order"] ?? data
                            : data;
                        var orders = AsMany(token)
                            .Select(value =>
                                value.ToObject<TradernetOrder>(
                                    serializer))
                            .Where(order => order is not null)
                            .ToArray();
                        await ordersHandler(
                            orders, cancellationToken);
                    }
                    break;
                case "error":
                    await RaiseError(
                        new InvalidOperationException(
                            "Tradernet WebSocket error: " +
                            data?.ToString(
                                Formatting.None)),
                        cancellationToken);
                    break;
                case "userData":
                case "heartbeat":
                    break;
                default:
                    this.AddDebugLog(
                        "Tradernet ignored WebSocket event '{0}'.",
                        eventName);
                    break;
            }
        }
        catch (JsonException error)
        {
            await RaiseError(new InvalidDataException(
                $"Invalid Tradernet '{eventName}' event.",
                error), cancellationToken);
        }
    }

    private ValueTask SendTickerList(string eventName,
        SynchronizedSet<string> tickers,
        CancellationToken cancellationToken)
        => _client.SendAsync(
            new JArray(
                eventName,
                JArray.FromObject(
                    tickers.SyncGet(values =>
                        values.OrderBy(value => value)
                            .ToArray())))
            .ToString(Formatting.None),
            cancellationToken);

    private ValueTask SendEvent(string eventName,
        CancellationToken cancellationToken)
        => _client.SendAsync(
            new JArray(eventName).ToString(Formatting.None),
            cancellationToken);

    private ValueTask RaiseError(Exception error,
        CancellationToken cancellationToken)
        => Error is { } handler
            ? handler(error, cancellationToken) : default;

    private static IEnumerable<JToken> AsMany(JToken token)
    {
        if (token is null ||
            token.Type == JTokenType.Null)
            return [];
        return token is JArray array
            ? array : [token];
    }

    private static string NormalizeTicker(string ticker)
        => ticker.ThrowIfEmpty(nameof(ticker))
            .Trim().ToUpperInvariant();

    private static string BuildAddress(
        Uri address, string sid)
    {
        if (sid.IsEmpty())
            return address.AbsoluteUri;
        var builder = new UriBuilder(address);
        var prefix = builder.Query
            .TrimStart('?');
        builder.Query =
            (prefix.IsEmpty() ? string.Empty :
                prefix + "&") +
            "SID=" + Uri.EscapeDataString(sid);
        return builder.Uri.AbsoluteUri;
    }

    protected override void DisposeManaged()
    {
        _client.PostConnect -= RestoreSubscriptions;
        _client.Dispose();
        base.DisposeManaged();
    }
}
