namespace StockSharp.Tardis;

readonly record struct TardisStreamKey(string Symbol, TardisStreamKinds Kind,
	TimeSpan TimeFrame);

static class TardisExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static string ToWire<TEnum>(this TEnum value)
		where TEnum : struct, Enum
		=> TardisEnumConverter<TEnum>.ToWire(value);

	public static string NormalizeExchange(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 128 || value.Any(character =>
			char.IsControl(character) || char.IsWhiteSpace(character) ||
			character is '?' or '#' or '&' or ',' or '/' or '\\' or ':'))
			throw new ArgumentException(
				"Tardis exchange ID contains unsupported characters.", nameof(value));
		return value.ToLowerInvariant();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static DateTime ParseTardisTime(this string value, string field)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"Tardis returned an invalid UTC {field} timestamp.");
		return result.EnsureUtc();
	}

	public static DateTime? TryParseTardisTime(this string value, string field)
		=> value.IsEmpty() ? null : value.ParseTardisTime(field);

	public static string FormatTardisTime(this DateTime value)
		=> value.EnsureUtc().ToString("O", CultureInfo.InvariantCulture);

	public static string ToBarDataType(this TimeSpan timeFrame)
	{
		if (!TimeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Tardis does not advertise the {timeFrame} candle interval.");
		if (timeFrame.Ticks % TimeSpan.TicksPerMinute == 0)
			return "trade_bar_" + ((long)timeFrame.TotalMinutes)
				.ToString(CultureInfo.InvariantCulture) + "m";
		return "trade_bar_" + ((long)timeFrame.TotalSeconds)
			.ToString(CultureInfo.InvariantCulture) + "s";
	}

	public static string[] ToDataTypes(this TardisStreamKey key)
		=> key.Kind switch
		{
			TardisStreamKinds.Trades => ["trade"],
			TardisStreamKinds.Level1 => ["quote", "derivative_ticker"],
			TardisStreamKinds.MarketDepth => ["book_change"],
			TardisStreamKinds.Candles => [key.TimeFrame.ToBarDataType()],
			_ => throw new ArgumentOutOfRangeException(nameof(key), key,
				"Unknown Tardis stream kind."),
		};

	public static SecurityTypes ToSecurityType(this TardisInstrumentTypes value)
		=> value switch
		{
			TardisInstrumentTypes.Spot => SecurityTypes.CryptoCurrency,
			TardisInstrumentTypes.Perpetual or TardisInstrumentTypes.Future or
				TardisInstrumentTypes.Combo => SecurityTypes.Future,
			TardisInstrumentTypes.Option => SecurityTypes.Option,
			_ => throw new InvalidDataException(
				"Tardis instrument has an unknown security type."),
		};

	public static OptionTypes? ToOptionType(this TardisOptionTypes value)
		=> value switch
		{
			TardisOptionTypes.Call => OptionTypes.Call,
			TardisOptionTypes.Put => OptionTypes.Put,
			_ => null,
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var result)
			? result
			: null;
}
