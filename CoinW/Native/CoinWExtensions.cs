namespace StockSharp.CoinW.Native;

static class CoinWExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
			[TimeSpan.FromDays(30)] = "1M",
		};

	private static readonly IReadOnlyDictionary<TimeSpan, string> _futuresGranularities =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "0",
			[TimeSpan.FromMinutes(3)] = "7",
			[TimeSpan.FromMinutes(5)] = "1",
			[TimeSpan.FromMinutes(15)] = "2",
			[TimeSpan.FromMinutes(30)] = "8",
			[TimeSpan.FromHours(1)] = "3",
			[TimeSpan.FromHours(4)] = "4",
			[TimeSpan.FromDays(1)] = "5",
			[TimeSpan.FromDays(7)] = "6",
			[TimeSpan.FromDays(30)] = "9",
		};

	private static readonly IReadOnlyDictionary<TimeSpan, string> _futuresWebSocketIntervals =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1",
			[TimeSpan.FromMinutes(3)] = "3",
			[TimeSpan.FromMinutes(5)] = "5",
			[TimeSpan.FromMinutes(15)] = "15",
			[TimeSpan.FromMinutes(30)] = "30",
			[TimeSpan.FromHours(1)] = "1H",
			[TimeSpan.FromHours(4)] = "4H",
			[TimeSpan.FromDays(1)] = "1D",
			[TimeSpan.FromDays(7)] = "1W",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToCoinWInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported CoinW candle interval.");

	public static string ToCoinWFuturesGranularity(this TimeSpan timeFrame)
		=> _futuresGranularities.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "Unsupported CoinW futures candle interval.");

	public static string ToCoinWFuturesWebSocketInterval(this TimeSpan timeFrame)
		=> _futuresWebSocketIntervals.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported CoinW futures WebSocket candle interval.");

	public static TimeSpan? FromCoinWWebSocketInterval(this string interval, CoinWSections section)
	{
		var source = section == CoinWSections.Spot ? TimeFrames : _futuresWebSocketIntervals;
		foreach (var pair in source)
		{
			if (pair.Value.EqualsIgnoreCase(interval))
				return pair.Key;
		}
		return null;
	}

	public static string ToBoardCode(this CoinWSections section)
		=> section == CoinWSections.Futures ? BoardCodes.CoinWFutures : BoardCodes.CoinW;

	public static CoinWSections ToSection(this string boardCode)
		=> boardCode.EqualsIgnoreCase(BoardCodes.CoinWFutures) ? CoinWSections.Futures : CoinWSections.Spot;

	public static SecurityId ToStockSharp(this string securityCode, CoinWSections section)
		=> new()
		{
			SecurityCode = securityCode?.ToUpperInvariant(),
			BoardCode = section.ToBoardCode(),
		};

	public static string ToCoinWNativeFuturesSymbol(this string securityCode)
	{
		securityCode = securityCode.ThrowIfEmpty(nameof(securityCode)).ToUpperInvariant();
		return securityCode.EndsWith("USDT", StringComparison.OrdinalIgnoreCase) &&
			!securityCode.Contains('_')
				? securityCode[..^4]
				: securityCode;
	}

	public static string ToCoinWFuturesSecurityCode(this string nativeSymbol, string quote = "USDT")
	{
		nativeSymbol = nativeSymbol.ThrowIfEmpty(nameof(nativeSymbol)).ToUpperInvariant();
		if (nativeSymbol.Contains('_') || nativeSymbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
			return nativeSymbol;
		return nativeSymbol + quote.ToUpperInvariant();
	}

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToUtcTime(this long milliseconds)
		=> (Math.Abs(milliseconds) < 100_000_000_000L
			? DateTimeOffset.FromUnixTimeSeconds(milliseconds)
			: DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)).UtcDateTime;

	public static DateTime ToCoinWUtcTime(this string value)
	{
		if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
			return timestamp.ToUtcTime();
		if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
			return DateTime.SpecifyKind(time, DateTimeKind.Utc);
		return DateTime.UtcNow;
	}

	public static long ToUnixMilliseconds(this DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static Sides ToStockSharpSide(this string side)
		=> side.EqualsIgnoreCase("buy") || side.EqualsIgnoreCase("long") || side == "0"
			? Sides.Buy
			: Sides.Sell;

	public static OrderStates ToSpotOrderState(this int status)
		=> status switch
		{
			1 or 2 => OrderStates.Active,
			3 or 4 => OrderStates.Done,
			_ => OrderStates.None,
		};

	public static OrderStates ToFuturesOrderState(this string status)
		=> status?.ToLowerInvariant() switch
		{
			"unfinished" or "unfinish" or "part" => OrderStates.Active,
			"finish" or "finished" or "cancel" or "cancelled" or "canceled" => OrderStates.Done,
			"fail" or "failed" or "reject" or "rejected" => OrderStates.Failed,
			_ => OrderStates.None,
		};
}
