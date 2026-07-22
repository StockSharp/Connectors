namespace StockSharp.ProBit.Native;

static class ProBitExtensions
{
	public static readonly IReadOnlyDictionary<TimeSpan, string> TimeFrames =
		new Dictionary<TimeSpan, string>
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(10)] = "10m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromMinutes(30)] = "30m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromHours(4)] = "4h",
			[TimeSpan.FromHours(6)] = "6h",
			[TimeSpan.FromHours(12)] = "12h",
			[TimeSpan.FromDays(1)] = "1D",
			[TimeSpan.FromDays(7)] = "1W",
			[TimeSpan.FromDays(30)] = "1M",
		};

	public static string ToProBitInterval(this TimeSpan timeFrame)
		=> TimeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame,
				"Unsupported ProBit candle interval.");

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol?.ToUpperInvariant(),
			BoardCode = BoardCodes.ProBit,
		};

	public static decimal? ToDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
			? number
			: null;

	public static string ToWire(this decimal value)
		=> value.ToString(CultureInfo.InvariantCulture);

	public static DateTime ToUtcTime(this string value)
		=> DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
			? DateTime.SpecifyKind(time, DateTimeKind.Utc)
			: DateTime.UtcNow;

	public static string ToWireTime(this DateTime value)
		=> value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

	public static decimal PrecisionToStep(this int precision)
		=> precision <= 0 ? 1m : 1m / (decimal)Math.Pow(10, precision);

	public static Sides ToStockSharpSide(this string side)
		=> side?.ToLowerInvariant() switch
		{
			"buy" => Sides.Buy,
			"sell" => Sides.Sell,
			_ => throw new InvalidDataException($"Unknown ProBit side '{side}'."),
		};

	public static OrderStates ToStockSharpOrderState(this string status)
		=> status?.ToLowerInvariant() switch
		{
			"open" => OrderStates.Active,
			"filled" or "cancelled" or "canceled" => OrderStates.Done,
			"rejected" or "failed" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	public static DateTime Align(this DateTime time, TimeSpan timeFrame)
	{
		time = time.ToUniversalTime();
		return new DateTime(time.Ticks / timeFrame.Ticks * timeFrame.Ticks, DateTimeKind.Utc);
	}
}
