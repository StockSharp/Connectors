namespace StockSharp.Foxbit.Native;

readonly record struct FoxbitQuery(string Encoded, string Signing);

static class FoxbitQueryWriter
{
    private sealed class Builder
    {
        private readonly List<string> _encoded = [];
        private readonly List<string> _signing = [];

        public void Add(string name, string value)
        {
            if (value.IsEmpty())
                return;
            _encoded.Add($"{name}={Uri.EscapeDataString(value)}");
            _signing.Add($"{name}={value}");
        }

        public void Add(string name, int? value)
        {
            if (value is not int actual)
                return;
            Add(name, actual.ToString(CultureInfo.InvariantCulture));
        }

        public FoxbitQuery Build()
            => new(string.Join('&', _encoded), string.Join('&', _signing));
    }

    public static FoxbitQuery Create(FoxbitPublicTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new Builder();
        builder.Add("start_time", Format(request.From, false));
        builder.Add("end_time", Format(request.To, false));
        builder.Add("page", request.Page);
        builder.Add("page_size", request.PageSize);
        return builder.Build();
    }

    public static FoxbitQuery Create(FoxbitCandlesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new Builder();
        builder.Add("interval", request.Interval);
        builder.Add("start_time", Format(request.From, true));
        builder.Add("end_time", Format(request.To, true));
        builder.Add("limit", request.Limit);
        builder.Add("order_direction", request.Direction);
        return builder.Build();
    }

    public static FoxbitQuery Create(FoxbitOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new Builder();
        builder.Add("start_time", Format(request.From, false));
        builder.Add("end_time", Format(request.To, false));
        builder.Add("page_size", request.PageSize);
        builder.Add("page", request.Page);
        builder.Add("market_symbol", request.MarketSymbol);
        builder.Add("state", request.State?.ToString().ToUpperInvariant());
        builder.Add("side", request.Side?.ToString().ToUpperInvariant());
        return builder.Build();
    }

    public static FoxbitQuery Create(FoxbitTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var builder = new Builder();
        builder.Add("market_symbol", request.MarketSymbol);
        builder.Add("order_id", request.OrderId);
        builder.Add("start_time", Format(request.From, false));
        builder.Add("end_time", Format(request.To, false));
        builder.Add("page", request.Page);
        builder.Add("page_size", request.PageSize);
        return builder.Build();
    }

    public static FoxbitQuery Depth(int depth)
    {
        var builder = new Builder();
        builder.Add("depth", depth.ToString(CultureInfo.InvariantCulture));
        return builder.Build();
    }

    private static string Format(DateTime? value, bool minutePrecision)
        => value is not DateTime actual
            ? null
            : actual.ToUtcTime().ToString(
                minutePrecision ? "yyyy-MM-dd'T'HH:mm" : "O",
                CultureInfo.InvariantCulture);
}
