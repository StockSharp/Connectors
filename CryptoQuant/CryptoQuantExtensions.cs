namespace StockSharp.CryptoQuant;

sealed class CryptoQuantInstrument
{
	public CryptoQuantInstrumentKinds Kind { get; init; }
	public string Namespace { get; init; }
	public string Token { get; init; }
	public CryptoQuantWindows[] Windows { get; init; }

	public string Key => Namespace + ":" + Token;
	public string Route => Namespace + "/market-data/price-ohlcv";
	public string Code => (Kind == CryptoQuantInstrumentKinds.Token
		? Token + "@" + Namespace
		: Namespace).ToUpperInvariant() + "/USD";
	public string Symbol => (Kind == CryptoQuantInstrumentKinds.Token
		? Token
		: Namespace).ToUpperInvariant();
}

static class CryptoQuantExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static CryptoQuantWindows ToWindow(this TimeSpan value)
		=> value switch
		{
			var interval when interval == TimeSpan.FromMinutes(1) =>
				CryptoQuantWindows.Minute,
			var interval when interval == TimeSpan.FromHours(1) =>
				CryptoQuantWindows.Hour,
			var interval when interval == TimeSpan.FromDays(1) =>
				CryptoQuantWindows.Day,
			_ => throw new NotSupportedException(
				$"CryptoQuant does not support the {value} price window."),
		};

	public static string ToWire<TEnum>(this TEnum value)
		where TEnum : struct, Enum
		=> CryptoQuantEnumConverter<TEnum>.ToWire(value);

	public static string NormalizeIdentifier(string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.Length > 128 || value.Any(character =>
			char.IsControl(character) || char.IsWhiteSpace(character) ||
			character is '?' or '#' or '&' or ',' or '/' or '\\' or ':'))
			throw new ArgumentException(
				"CryptoQuant identifier contains unsupported characters.", name);
		return value.ToLowerInvariant();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static string FormatCryptoQuantTime(this DateTime value)
		=> value.EnsureUtc().ToString("yyyyMMdd'T'HHmmss",
			CultureInfo.InvariantCulture);

	public static DateTime ParseCryptoQuantTime(this CryptoQuantOhlcv value)
	{
		var text = value?.DateTime.IsEmpty(value?.Date)?.Trim();
		if (text.IsEmpty())
			throw new InvalidDataException(
				"CryptoQuant OHLCV row has no timestamp.");
		var formats = new[]
		{
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd'T'HH:mm:ss",
			"yyyy-MM-dd",
			"yyyyMMdd'T'HHmmss",
			"yyyyMMdd",
		};
		if (!System.DateTime.TryParseExact(text, formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				"CryptoQuant returned an invalid UTC timestamp.");
		return result.Kind == DateTimeKind.Utc
			? result
			: System.DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}
}
