namespace StockSharp.Synthetix.Native;

static class SynthetixExtensions
{
	public static Uri NormalizeSynthetixSocketEndpoint(this string endpoint,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("ws" or "wss"))
			throw new ArgumentException(
				"Synthetix WebSocket endpoint must use WS or WSS.",
				parameterName);
		return uri;
	}

	public static SecurityId ToSynthetixSecurityId(this string symbol)
		=> new()
		{
			SecurityCode = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
				.ToUpperInvariant(),
			BoardCode = BoardCodes.Synthetix,
		};

	public static decimal ParseSynthetixDecimal(this string value, string field)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: throw new InvalidDataException(
				$"Synthetix returned invalid {field} '{value}'.");

	public static decimal? TryParseSynthetixDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static string ToSynthetixDecimal(this decimal value)
		=> value.ToString("0.############################",
			CultureInfo.InvariantCulture);

	public static DateTime FromSynthetixMilliseconds(this long value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return DateTime.UnixEpoch.AddMilliseconds(value);
	}

	public static long ToSynthetixMilliseconds(this DateTime value)
	{
		value = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
		return checked((long)(value - DateTime.UnixEpoch).TotalMilliseconds);
	}

	public static DateTime ParseSynthetixTime(this string value, string field)
	{
		if (long.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var milliseconds) &&
			milliseconds > 0)
			return milliseconds.FromSynthetixMilliseconds();
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"Synthetix returned invalid {field} '{value}'.");
		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static Sides ToStockSharpSide(this string value)
		=> value?.Trim().ToLowerInvariant() switch
		{
			"buy" or "long" => Sides.Buy,
			"sell" or "short" => Sides.Sell,
			_ => throw new InvalidDataException(
				$"Synthetix returned unknown side '{value}'."),
		};

	public static string ToSynthetixSide(this Sides value)
		=> value switch
		{
			Sides.Buy => "buy",
			Sides.Sell => "sell",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static OrderStates ToStockSharpOrderState(this string value)
	{
		value = value?.Replace("_", string.Empty,
			StringComparison.Ordinal).Replace(" ", string.Empty,
				StringComparison.Ordinal).ToLowerInvariant();
		return value switch
		{
			"started" or "new" or "placed" or "resting" or "modified" or
				"orderstateplaced" => OrderStates.Active,
			"partiallyfilled" or "orderstatepartiallyfilled" =>
				OrderStates.Active,
			"filled" or "cancelled" or "canceled" or "orderstatefilled" or
				"orderstatecancelled" => OrderStates.Done,
			"rejected" or "error" or "orderstaterejected" => OrderStates.Failed,
			"cancelling" or "modifying" => OrderStates.Pending,
			_ => OrderStates.Pending,
		};
	}

	public static OrderTypes ToStockSharpOrderType(this string value)
	{
		value = value?.Trim().ToLowerInvariant();
		if (value?.Contains("trigger", StringComparison.Ordinal) == true ||
			value?.Contains("stop", StringComparison.Ordinal) == true ||
			value?.Contains("takeprofit", StringComparison.Ordinal) == true)
			return OrderTypes.Conditional;
		return value?.Contains("market", StringComparison.Ordinal) == true
			? OrderTypes.Market
			: OrderTypes.Limit;
	}

	public static TimeSpan? FromSynthetixInterval(this string value)
		=> value switch
		{
			"1m" => TimeSpan.FromMinutes(1),
			"3m" => TimeSpan.FromMinutes(3),
			"5m" => TimeSpan.FromMinutes(5),
			"15m" => TimeSpan.FromMinutes(15),
			"30m" => TimeSpan.FromMinutes(30),
			"1h" => TimeSpan.FromHours(1),
			"2h" => TimeSpan.FromHours(2),
			"4h" => TimeSpan.FromHours(4),
			"6h" => TimeSpan.FromHours(6),
			"8h" => TimeSpan.FromHours(8),
			"12h" => TimeSpan.FromHours(12),
			"1d" => TimeSpan.FromDays(1),
			"3d" => TimeSpan.FromDays(3),
			"1w" => TimeSpan.FromDays(7),
			_ => null,
		};

	public static string ToSynthetixInterval(this TimeSpan value)
		=> value == TimeSpan.FromMinutes(1) ? "1m" :
			value == TimeSpan.FromMinutes(3) ? "3m" :
			value == TimeSpan.FromMinutes(5) ? "5m" :
			value == TimeSpan.FromMinutes(15) ? "15m" :
			value == TimeSpan.FromMinutes(30) ? "30m" :
			value == TimeSpan.FromHours(1) ? "1h" :
			value == TimeSpan.FromHours(2) ? "2h" :
			value == TimeSpan.FromHours(4) ? "4h" :
			value == TimeSpan.FromHours(6) ? "6h" :
			value == TimeSpan.FromHours(8) ? "8h" :
			value == TimeSpan.FromHours(12) ? "12h" :
			value == TimeSpan.FromDays(1) ? "1d" :
			value == TimeSpan.FromDays(3) ? "3d" :
			value == TimeSpan.FromDays(7) ? "1w" :
			throw new NotSupportedException(
				$"Synthetix does not support candle interval '{value}'.");
}
