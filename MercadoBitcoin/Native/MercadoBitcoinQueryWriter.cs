namespace StockSharp.MercadoBitcoin.Native;

static class MercadoBitcoinQueryWriter
{
    public static string Create(MercadoBitcoinSymbolsRequest request)
        => CreateSymbols(request?.Symbols);

    public static string Create(MercadoBitcoinTickersRequest request)
        => CreateSymbols(request?.Symbols);

    public static string Create(MercadoBitcoinOrderBookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"limit={request.Limit}";
    }

    public static string Create(MercadoBitcoinTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "tid", request.TradeId);
        Add(builder, "since", request.SinceTradeId);
        Add(builder, "from", request.From);
        Add(builder, "to", request.To);
        Add(builder, "limit", request.Limit);
        return builder.ToString();
    }

    public static string Create(MercadoBitcoinCandlesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "symbol", request.Symbol);
        Add(builder, "resolution", request.Resolution);
        Add(builder, "from", request.From);
        Add(builder, "to", request.To);
        Add(builder, "countback", request.CountBack);
        return builder.ToString();
    }

    public static string Create(MercadoBitcoinListOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "has_executions", request.HasExecutions);
        Add(builder, "side", request.Side?.ToWire());
        Add(builder, "status", request.Status?.ToWire());
        Add(builder, "created_at_from", request.CreatedAtFrom);
        Add(builder, "created_at_to", request.CreatedAtTo);
        Add(builder, "executed_at_from", request.ExecutedAtFrom);
        Add(builder, "executed_at_to", request.ExecutedAtTo);
        return builder.ToString();
    }

    public static string Create(MercadoBitcoinListAllOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "has_executions", request.HasExecutions);
        Add(builder, "symbol", request.Symbol);
        Add(builder, "status", request.Statuses is { Length: > 0 }
            ? string.Join(',', request.Statuses.Select(static value => value.ToWire()))
            : null);
        Add(builder, "size", request.Size);
        return builder.ToString();
    }

    public static string Create(MercadoBitcoinCancelOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"async={request.IsAsync.ToString().ToLowerInvariant()}";
    }

    public static string Create(MercadoBitcoinCancelAllRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new StringBuilder();
        Add(builder, "has_executions", request.HasExecutions);
        Add(builder, "symbol", request.Symbol);
        return builder.ToString();
    }

    private static string CreateSymbols(IEnumerable<string> symbols)
    {
        var values = (symbols ?? []).Where(static value => !value.IsEmpty())
            .Select(static value => value.NormalizeSymbol()).Distinct(
                StringComparer.OrdinalIgnoreCase).ToArray();
        return values.Length == 0
            ? null
            : $"symbols={Escape(string.Join(',', values))}";
    }

    private static void Add(StringBuilder builder, string name, string value)
    {
        if (value.IsEmpty())
            return;
        if (builder.Length > 0)
            builder.Append('&');
        builder.Append(name).Append('=').Append(Escape(value));
    }

    private static void Add(StringBuilder builder, string name, long? value)
    {
        if (value is not null)
            Add(builder, name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Add(StringBuilder builder, string name, int? value)
    {
        if (value is not null)
            Add(builder, name, value.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static void Add(StringBuilder builder, string name, bool? value)
    {
        if (value is not null)
            Add(builder, name, value.Value.ToString().ToLowerInvariant());
    }

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());
}
