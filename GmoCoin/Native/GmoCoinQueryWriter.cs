namespace StockSharp.GmoCoin.Native;

static class GmoCoinQueryWriter
{
    public static string Create(GmoCoinTickerRequest request)
        => request?.Symbol.IsEmpty() == false
            ? $"symbol={Escape(request.Symbol)}"
            : null;

    public static string Create(GmoCoinOrderBookRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}";
    }

    public static string Create(GmoCoinTradesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}&page={request.Page}&count={request.Count}";
    }

    public static string Create(GmoCoinKlinesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}&interval={Escape(request.Interval)}&date={Escape(request.Date)}";
    }

    public static string Create(GmoCoinOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"orderId={Escape(request.OrderIds)}";
    }

    public static string Create(GmoCoinActiveOrdersRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}&page={request.Page}&count={request.Count}";
    }

    public static string Create(GmoCoinExecutionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var value = !request.ExecutionIds.IsEmpty()
            ? $"executionId={Escape(request.ExecutionIds)}"
            : $"orderId={Escape(request.OrderIds)}";
        return value;
    }

    public static string Create(GmoCoinLatestExecutionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}&page={request.Page}&count={request.Count}";
    }

    public static string Create(GmoCoinOpenPositionsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return $"symbol={Escape(request.Symbol)}&page={request.Page}&count={request.Count}";
    }

    public static string Create(GmoCoinPositionSummaryRequest request)
        => request?.Symbol.IsEmpty() == false
            ? $"symbol={Escape(request.Symbol)}"
            : null;

    private static string Escape(string value)
        => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());
}
