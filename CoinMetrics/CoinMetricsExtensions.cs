namespace StockSharp.CoinMetrics;

readonly record struct CoinMetricsStreamKey(
	CoinMetricsStreamKinds Kind,
	string Market,
	TimeSpan TimeFrame,
	CoinMetricsBookDepthModes BookDepth);

static class CoinMetricsExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static string NormalizeMarket(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 512 || value.Any(character =>
			char.IsControl(character) || character is '?' or '#' or '&' or ','))
			throw new ArgumentException(
				"Coin Metrics market identifier contains unsupported characters.",
				nameof(value));
		return value;
	}

	public static CoinMetricsCandleFrequencies ToFrequency(this TimeSpan value)
		=> value switch
		{
			var interval when interval == TimeSpan.FromMinutes(1) =>
				CoinMetricsCandleFrequencies.OneMinute,
			var interval when interval == TimeSpan.FromMinutes(5) =>
				CoinMetricsCandleFrequencies.FiveMinutes,
			var interval when interval == TimeSpan.FromMinutes(10) =>
				CoinMetricsCandleFrequencies.TenMinutes,
			var interval when interval == TimeSpan.FromMinutes(15) =>
				CoinMetricsCandleFrequencies.FifteenMinutes,
			var interval when interval == TimeSpan.FromMinutes(30) =>
				CoinMetricsCandleFrequencies.ThirtyMinutes,
			var interval when interval == TimeSpan.FromHours(1) =>
				CoinMetricsCandleFrequencies.OneHour,
			var interval when interval == TimeSpan.FromHours(4) =>
				CoinMetricsCandleFrequencies.FourHours,
			var interval when interval == TimeSpan.FromDays(1) =>
				CoinMetricsCandleFrequencies.OneDay,
			_ => throw new NotSupportedException(
				$"Coin Metrics does not support the {value} candle interval."),
		};

	public static string ToWire<TEnum>(this TEnum value)
		where TEnum : struct, Enum
		=> CoinMetricsEnumConverter<TEnum>.ToWire(value);

	public static string ToPath(this CoinMetricsStreamKinds value)
		=> value switch
		{
			CoinMetricsStreamKinds.Trades => "market-trades",
			CoinMetricsStreamKinds.Quotes => "market-quotes",
			CoinMetricsStreamKinds.OrderBooks => "market-orderbooks",
			CoinMetricsStreamKinds.Candles => "market-candles",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Coin Metrics stream kind is unsupported."),
		};

	public static DateTime ParseCoinMetricsTime(this string value, string field)
	{
		value = value.ThrowIfEmpty(field).Trim();
		if (!value.EndsWith('Z'))
			throw new InvalidDataException(
				$"Coin Metrics {field} timestamp is not UTC.");
		var separator = value.LastIndexOf('.');
		if (separator >= 0)
		{
			var fractionLength = value.Length - separator - 2;
			if (fractionLength > 7)
				value = value.Remove(separator + 8, fractionLength - 7);
		}
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"Coin Metrics {field} timestamp is invalid.");
		return result.Kind == DateTimeKind.Utc
			? result
			: DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static DateTime ParseCoinMetricsReferenceTime(this string value,
		string field)
	{
		value = value.ThrowIfEmpty(field).Trim();
		if (DateTime.TryParseExact(value, "yyyy-MM-dd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
			return DateTime.SpecifyKind(date, DateTimeKind.Utc);
		return value.ParseCoinMetricsTime(field);
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string FormatCoinMetricsTime(this DateTime value)
		=> value.EnsureUtc().ToString(
			"yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

	public static long ParseCoinMetricsSequence(this string value,
		string field)
		=> long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var sequence) && sequence >= 0
				? sequence
				: throw new InvalidDataException(
					$"Coin Metrics {field} sequence is invalid.");

	public static bool IsCanonicalMarketId(this string value)
		=> !value.IsEmpty() &&
			(value.EndsWithIgnoreCase("-spot") ||
			 value.EndsWithIgnoreCase("-future") ||
			 value.EndsWithIgnoreCase("-option"));
}
