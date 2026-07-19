namespace StockSharp.CoinJar.Native;

static class CoinJarQueryWriter
{
    public static string Create(CoinJarTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = new List<string>();
        Add(values, "before", request.Before);
        Add(values, "after", request.After);
        Add(values, "limit", request.Limit);
        return string.Join("&", values);
    }

    public static string Create(CoinJarCandlesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = new List<string>();
        Add(values, "before", request.Before);
        Add(values, "after", request.After);
        Add(values, "interval", request.Interval);
        return string.Join("&", values);
    }

    public static string Create(CoinJarCursorRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = new List<string>();
        Add(values, "cursor", request.Cursor);
        return string.Join("&", values);
    }

    private static void Add(List<string> values, string name, long? value)
    {
        if (value is long number)
            values.Add($"{name}={number.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void Add(List<string> values, string name, int? value)
    {
        if (value is int number)
            values.Add($"{name}={number.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void Add(List<string> values, string name, string value)
    {
        if (!value.IsEmpty())
            values.Add($"{name}={Uri.EscapeDataString(value)}");
    }
}
