namespace StockSharp.LCX.Native;

static class LcxExtensions
{
	public static SecurityId ToStockSharp(this LcxMarket market)
		=> new()
		{
			SecurityCode = market?.Symbol ??
				throw new ArgumentNullException(nameof(market)),
			BoardCode = BoardCodes.Lcx,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(
			value, true, out var currency)
				? currency
				: null;

	public static decimal? ToStep(this int precision)
	{
		if (precision < 0 || precision > 28)
			return null;
		var result = 1m;
		for (var index = 0; index < precision; index++)
			result /= 10m;
		return result;
	}

	public static Sides ToLcxSide(this string value)
		=> value.EqualsIgnoreCase("BUY")
			? Sides.Buy
			: Sides.Sell;

	public static string ToLcx(this Sides side)
		=> side == Sides.Buy ? "BUY" : "SELL";

	public static OrderTypes ToLcxOrderType(this string value)
		=> value.EqualsIgnoreCase("MARKET")
			? OrderTypes.Market
			: OrderTypes.Limit;

	public static string ToLcx(this OrderTypes type)
		=> type == OrderTypes.Market ? "MARKET" : "LIMIT";

	public static OrderStates ToLcxOrderState(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"OPEN" or "PARTIAL" or "PARTIALLY_FILLED" =>
				OrderStates.Active,
			"CLOSED" or "FILLED" or "CANCEL" or "CANCELLED" =>
				OrderStates.Done,
			"REJECTED" or "FAILED" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime FromLcxTimestamp(this long value)
		=> DateTimeOffset.FromUnixTimeMilliseconds(
			value < 100_000_000_000
				? value * 1000
				: value).UtcDateTime;
}
