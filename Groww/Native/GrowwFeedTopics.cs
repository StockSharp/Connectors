namespace StockSharp.Groww.Native;

internal static class GrowwFeedTopics
{
	public static string GetPrice(GrowwSecurityInfo info)
	{
		ValidateMarket(info);
		var asset = info.Segment.EqualsIgnoreCase("FNO") ? "fo" : "eq";
		return $"/ld/{asset}/{info.Exchange.ToLowerInvariant()}/price.{RequireToken(info)}";
	}

	public static string GetDepth(GrowwSecurityInfo info)
	{
		ValidateMarket(info);
		if (IsIndex(info))
			throw new NotSupportedException("Groww does not publish market depth for indices.");
		var asset = info.Segment.EqualsIgnoreCase("FNO") ? "fo" : "eq";
		return $"/ld/{asset}/{info.Exchange.ToLowerInvariant()}/book.{RequireToken(info)}";
	}

	public static string GetIndex(GrowwSecurityInfo info)
	{
		ValidateMarket(info);
		return $"/ld/indices/{info.Exchange.ToLowerInvariant()}/price.{RequireToken(info)}";
	}

	public static string GetEquityOrders(string subscriptionId)
		=> $"stocks/order/updates.apex.{subscriptionId.ThrowIfEmpty(nameof(subscriptionId))}";

	public static string GetDerivativeOrders(string subscriptionId)
		=> $"stocks_fo/order/updates.apex.{subscriptionId.ThrowIfEmpty(nameof(subscriptionId))}";

	public static string GetDerivativePositions(string subscriptionId)
		=> $"stocks_fo/position/updates.apex.{subscriptionId.ThrowIfEmpty(nameof(subscriptionId))}";

	public static bool IsIndex(GrowwSecurityInfo info)
		=> info.InstrumentType is not null &&
			(info.InstrumentType.EqualsIgnoreCase("IDX") || info.InstrumentType.EqualsIgnoreCase("INDEX"));

	private static string RequireToken(GrowwSecurityInfo info)
		=> info.ExchangeToken.ThrowIfEmpty(nameof(info.ExchangeToken));

	private static void ValidateMarket(GrowwSecurityInfo info)
	{
		if (info.Segment.EqualsIgnoreCase("COMMODITY") || info.Exchange.EqualsIgnoreCase("MCX"))
			throw new NotSupportedException("Groww's NATS feed does not currently publish MCX commodity subjects; REST trading remains available.");
		if (!info.Segment.EqualsIgnoreCase("CASH") && !info.Segment.EqualsIgnoreCase("FNO"))
			throw new NotSupportedException($"Groww streaming does not support segment '{info.Segment}'.");
		if (!info.Exchange.EqualsIgnoreCase("NSE") && !info.Exchange.EqualsIgnoreCase("BSE"))
			throw new NotSupportedException($"Groww streaming does not support exchange '{info.Exchange}'.");
	}
}
