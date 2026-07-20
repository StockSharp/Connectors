namespace StockSharp.Nado.Native;

static class NadoExtensions
{
	private const decimal _x18 = 1_000_000_000_000_000_000m;

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromDays(28),
	];

	public static decimal ParseX18(this string value, string field)
		=> ParseInteger(value, field) / _x18;

	public static decimal? TryParseX18(this string value)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var number))
			return null;
		return ToDecimal(number, "x18 value") / _x18;
	}

	public static decimal ParseAmount(this string value, string field)
		=> ParseInteger(value, field) / _x18;

	public static decimal? TryParseAmount(this string value)
		=> value.TryParseX18();

	public static string ToX18(this decimal value, string field)
	{
		var scaled = value * _x18;
		if (scaled != decimal.Truncate(scaled))
			throw new InvalidOperationException("Nado " + field +
				" cannot be represented with 18 decimals.");
		return scaled.ToString("0", CultureInfo.InvariantCulture);
	}

	public static long ToNanoseconds(this DateTime time)
		=> checked((time.EnsureNadoUtc() - DateTime.UnixEpoch).Ticks * 100L);

	public static DateTime FromNadoNanoseconds(this string value)
	{
		var nanoseconds = ParseInteger(value, "timestamp");
		return DateTime.UnixEpoch.AddTicks((long)(nanoseconds / 100));
	}

	public static DateTime FromNadoSeconds(this string value)
	{
		if (!long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var seconds))
			throw new InvalidDataException("Nado timestamp is invalid.");
		return DateTime.UnixEpoch.AddSeconds(seconds);
	}

	public static DateTime FromNadoMilliseconds(this string value)
	{
		if (!long.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var milliseconds))
			throw new InvalidDataException("Nado timestamp is invalid.");
		return DateTime.UnixEpoch.AddMilliseconds(milliseconds);
	}

	public static DateTime FromNadoSeconds(this long value)
		=> DateTime.UnixEpoch.AddSeconds(value);

	public static DateTime FromNadoOrderTime(this string value)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var timestamp) || timestamp < 0)
			throw new InvalidDataException("Nado order timestamp is invalid.");
		var maximum = new BigInteger((DateTime.MaxValue - DateTime.UnixEpoch)
			.TotalSeconds);
		if (timestamp > maximum && timestamp / 1000 <= maximum)
			timestamp /= 1000;
		if (timestamp > maximum)
			return DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
		return DateTime.UnixEpoch.AddSeconds((long)timestamp);
	}

	public static DateTime EnsureNadoUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static int ToGranularity(this TimeSpan timeFrame)
	{
		var seconds = checked((int)timeFrame.TotalSeconds);
		return seconds switch
		{
			60 or 300 or 900 or 3600 or 7200 or 14400 or 86400 or
				604800 or 2419200 => seconds,
			_ => throw new NotSupportedException(
				"Nado does not support the " + timeFrame + " candle interval."),
		};
	}

	public static string ToWire(this NadoOrderExecutionTypes value)
		=> value switch
		{
			NadoOrderExecutionTypes.Default => "default",
			NadoOrderExecutionTypes.ImmediateOrCancel => "ioc",
			NadoOrderExecutionTypes.FillOrKill => "fok",
			NadoOrderExecutionTypes.PostOnly => "post_only",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static TimeInForce? ToStockSharp(this NadoOrderExecutionTypes value)
		=> value switch
		{
			NadoOrderExecutionTypes.Default => TimeInForce.PutInQueue,
			NadoOrderExecutionTypes.ImmediateOrCancel => TimeInForce.CancelBalance,
			NadoOrderExecutionTypes.FillOrKill => TimeInForce.MatchOrCancel,
			NadoOrderExecutionTypes.PostOnly => TimeInForce.PutInQueue,
			_ => null,
		};

	public static NadoOrderExecutionTypes ToNado(this TimeInForce? value,
		bool isMarket)
		=> value switch
		{
			TimeInForce.CancelBalance => NadoOrderExecutionTypes.ImmediateOrCancel,
			TimeInForce.MatchOrCancel => NadoOrderExecutionTypes.FillOrKill,
			_ when isMarket => NadoOrderExecutionTypes.ImmediateOrCancel,
			_ => NadoOrderExecutionTypes.Default,
		};

	public static SecurityTypes ToStockSharp(this NadoProductTypes value)
		=> value switch
		{
			NadoProductTypes.Spot => SecurityTypes.CryptoCurrency,
			NadoProductTypes.Perpetual => SecurityTypes.Future,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
		};

	public static SecurityId ToStockSharp(this string symbol)
		=> new()
		{
			SecurityCode = symbol,
			BoardCode = BoardCodes.Nado,
		};

	private static decimal ParseInteger(string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var number))
			throw new InvalidDataException("Nado " + field + " is invalid.");
		return ToDecimal(number, field);
	}

	private static decimal ToDecimal(BigInteger value, string field)
	{
		if (value < (BigInteger)decimal.MinValue ||
			value > (BigInteger)decimal.MaxValue)
			throw new InvalidDataException("Nado " + field +
				" exceeds decimal range.");
		return (decimal)value;
	}
}
