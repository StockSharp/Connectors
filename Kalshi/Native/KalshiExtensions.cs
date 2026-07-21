namespace StockSharp.Kalshi.Native;

static class KalshiExtensions
{
	public static string NormalizeHttpEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!value.Contains("://", StringComparison.Ordinal))
			value = "https://" + value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Kalshi REST endpoints must be absolute HTTPS URIs.", name);
		return value.TrimEnd('/') + "/";
	}

	public static string NormalizeSocketEndpoint(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!value.Contains("://", StringComparison.Ordinal))
			value = "wss://" + value.TrimStart('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != "wss" || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Kalshi WebSocket endpoints must be absolute WSS URIs.", name);
		return value;
	}

	public static decimal ParseKalshiDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Kalshi returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal? TryParseKalshiDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ? result : null;

	public static DateTime? TryParseKalshiTime(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			return null;
		return result.EnsureUtc();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc
			? value
			: value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

	public static DateTime FromKalshiMilliseconds(this long value)
		=> value > 0 ? DateTime.UnixEpoch.AddMilliseconds(value) : DateTime.UtcNow;

	public static DateTime FromKalshiSeconds(this long value)
		=> value > 0 ? DateTime.UnixEpoch.AddSeconds(value) : DateTime.UtcNow;

	public static long ToKalshiSeconds(this DateTime value)
		=> checked((long)(value.EnsureUtc() - DateTime.UnixEpoch).TotalSeconds);

	public static string ToKalshiPrice(this decimal value)
	{
		if (value is <= 0 or >= 1 || value * 1_000_000m !=
			decimal.Truncate(value * 1_000_000m))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi prices must be between zero and one with at most six decimals.");
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}

	public static string ToKalshiCount(this decimal value)
	{
		if (value <= 0 || value * 100m != decimal.Truncate(value * 100m))
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"Kalshi volume must be positive with at most two decimals.");
		return value.ToString("0.##", CultureInfo.InvariantCulture);
	}

	public static KalshiBookSides ToKalshi(this Sides side)
		=> side == Sides.Buy ? KalshiBookSides.Bid : KalshiBookSides.Ask;

	public static Sides ToStockSharp(this KalshiBookSides side)
		=> side == KalshiBookSides.Bid ? Sides.Buy : Sides.Sell;

	public static Sides ToStockSharp(this KalshiMarketSides side)
		=> side == KalshiMarketSides.Yes ? Sides.Buy : Sides.Sell;

	public static TimeInForce ToStockSharp(this KalshiTimeInForces value)
		=> value switch
		{
			KalshiTimeInForces.FillOrKill => TimeInForce.MatchOrCancel,
			KalshiTimeInForces.ImmediateOrCancel => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static OrderStates ToStockSharp(this KalshiOrderStatuses value)
		=> value switch
		{
			KalshiOrderStatuses.Resting => OrderStates.Active,
			KalshiOrderStatuses.Canceled or KalshiOrderStatuses.Executed =>
				OrderStates.Done,
			_ => OrderStates.Pending,
		};

	public static SecurityId ToStockSharp(this KalshiMarket market)
		=> new()
		{
			SecurityCode = market.Ticker,
			BoardCode = BoardCodes.Kalshi,
			Native = market.Ticker,
		};

	public static bool IsTrading(this KalshiMarket market)
		=> market?.Status == KalshiMarketStatuses.Active;

	public static decimal GetPriceStep(this KalshiMarket market)
		=> (market?.PriceRanges ?? [])
			.Select(static range => range?.Step.TryParseKalshiDecimal())
			.Where(static step => step is > 0)
			.Select(static step => step.Value)
			.DefaultIfEmpty(0.01m)
			.Min();

	public static bool IsPriceValid(this KalshiMarket market, decimal price)
	{
		var ranges = market?.PriceRanges;
		if (ranges?.Length is not > 0)
			return price is > 0 and < 1;
		foreach (var range in ranges)
		{
			var start = range?.Start.TryParseKalshiDecimal();
			var end = range?.End.TryParseKalshiDecimal();
			var step = range?.Step.TryParseKalshiDecimal();
			if (start is null || end is null || step is not > 0 ||
				price < start || price > end)
				continue;
			var offset = (price - start.Value) / step.Value;
			if (offset == decimal.Truncate(offset))
				return true;
		}
		return false;
	}
}
