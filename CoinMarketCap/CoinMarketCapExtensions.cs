namespace StockSharp.CoinMarketCap;

readonly record struct CoinMarketCapSecurityKey(
	int Id,
	string QuoteCurrency,
	string Symbol,
	string Name,
	string Slug)
{
	public string ToNative()
		=> string.Join('|', Id.ToString(CultureInfo.InvariantCulture),
			Escape(QuoteCurrency), Escape(Symbol), Escape(Name), Escape(Slug));

	public static bool TryParse(string value, out CoinMarketCapSecurityKey key)
	{
		key = default;
		if (value.IsEmpty())
			return false;
		var parts = value.Split('|');
		if (parts.Length != 5 ||
			!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
				out var id) || id <= 0)
			return false;
		key = new(id, Unescape(parts[1]), Unescape(parts[2]),
			Unescape(parts[3]), Unescape(parts[4]));
		return !key.QuoteCurrency.IsEmpty() && !key.Symbol.IsEmpty();
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static string Unescape(string value)
		=> Uri.UnescapeDataString(value ?? string.Empty);
}

static class CoinMarketCapExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static string GetApiEndpoint(this CoinMarketCapAccessModes mode)
		=> mode switch
		{
			CoinMarketCapAccessModes.Keyless =>
				"https://pro-api.coinmarketcap.com/public-api",
			CoinMarketCapAccessModes.ApiKey =>
				"https://pro-api.coinmarketcap.com",
			_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
		};

	public static string NormalizeCurrency(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 32 || value.Any(char.IsWhiteSpace))
			throw new ArgumentException(
				"CoinMarketCap quote currency must be a compact symbol up to 32 characters.",
				nameof(value));
		return value;
	}

	public static SecurityId ToSecurityId(this CoinMarketCapSecurityKey key)
		=> new()
		{
			SecurityCode = key.Symbol.ToUpperInvariant() + "/" +
				key.QuoteCurrency.ToUpperInvariant(),
			BoardCode = BoardCodes.CoinMarketCap,
			Native = key.ToNative(),
		};

	public static CoinMarketCapTimePeriods ToTimePeriod(this TimeSpan value)
		=> value == TimeSpan.FromHours(1)
			? CoinMarketCapTimePeriods.Hourly
			: value == TimeSpan.FromDays(1)
				? CoinMarketCapTimePeriods.Daily
				: throw new NotSupportedException(
					$"CoinMarketCap does not support the {value} candle interval.");

	public static DateTime ParseCoinMarketCapTime(this string value)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"Invalid CoinMarketCap timestamp '{value}'.");
		return result.EnsureUtc();
	}

	public static DateTime ToCoinMarketCapTime(this long milliseconds)
		=> DateTime.UnixEpoch.AddMilliseconds(milliseconds);

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};
}
