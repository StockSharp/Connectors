namespace StockSharp.BTCMarkets.Native;

static class BTCMarketsQueryWriter
{
    public static string Create(BTCMarketsTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new StringBuilder();
        Append(result, "limit", request.Limit > 0
            ? request.Limit.ToString(CultureInfo.InvariantCulture)
            : null);
        Append(result, "before", request.Before);
        Append(result, "after", request.After);
        return result.ToString();
    }

    public static string Create(BTCMarketsCandlesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new StringBuilder();
        Append(result, "timeWindow", request.TimeWindow);
        Append(result, "from", FormatTime(request.From));
        Append(result, "to", FormatTime(request.To));
        Append(result, "limit", request.Limit is > 0
            ? request.Limit.Value.ToString(CultureInfo.InvariantCulture)
            : null);
        Append(result, "before", request.Before);
        Append(result, "after", request.After);
        return result.ToString();
    }

    public static string Create(BTCMarketsOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new StringBuilder();
        Append(result, "marketId", request.MarketId);
        Append(result, "status", request.Status == BTCMarketsOrderQueryStatuses.Open
            ? "open"
            : "all");
        Append(result, "limit", request.Limit is > 0
            ? request.Limit.Value.ToString(CultureInfo.InvariantCulture)
            : null);
        Append(result, "before", request.Before);
        Append(result, "after", request.After);
        return result.ToString();
    }

    public static string Create(BTCMarketsUserTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new StringBuilder();
        Append(result, "marketId", request.MarketId);
        Append(result, "orderId", request.OrderId);
        Append(result, "limit", request.Limit is > 0
            ? request.Limit.Value.ToString(CultureInfo.InvariantCulture)
            : null);
        Append(result, "before", request.Before);
        Append(result, "after", request.After);
        return result.ToString();
    }

    public static string Create(BTCMarketsCancelOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = new StringBuilder();
        foreach (var marketId in request.MarketIds ?? [])
            Append(result, "marketId", marketId);
        return result.ToString();
    }

    private static string FormatTime(DateTime? value)
        => value?.ToUtcTime().ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
            CultureInfo.InvariantCulture);

    private static void Append(StringBuilder result, string name, string value)
    {
        if (value.IsEmpty())
            return;
        if (result.Length > 0)
            result.Append('&');
        result.Append(Uri.EscapeDataString(name));
        result.Append('=');
        result.Append(Uri.EscapeDataString(value));
    }
}
