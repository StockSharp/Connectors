namespace StockSharp.Pyth;

static class PythExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static string ToWire<TEnum>(this TEnum value)
		where TEnum : struct, Enum
		=> PythEnumConverter<TEnum>.ToWire(value);

	public static string ToResolution(this TimeSpan value)
		=> value switch
		{
			var interval when interval == TimeSpan.FromMinutes(1) => "1",
			var interval when interval == TimeSpan.FromMinutes(2) => "2",
			var interval when interval == TimeSpan.FromMinutes(5) => "5",
			var interval when interval == TimeSpan.FromMinutes(15) => "15",
			var interval when interval == TimeSpan.FromMinutes(30) => "30",
			var interval when interval == TimeSpan.FromHours(1) => "60",
			var interval when interval == TimeSpan.FromHours(2) => "120",
			var interval when interval == TimeSpan.FromHours(4) => "240",
			var interval when interval == TimeSpan.FromHours(6) => "360",
			var interval when interval == TimeSpan.FromHours(12) => "720",
			var interval when interval == TimeSpan.FromDays(1) => "D",
			var interval when interval == TimeSpan.FromDays(7) => "W",
			_ => throw new NotSupportedException(
				$"Pyth does not support the {value} candle resolution."),
		};

	public static PythChannels SelectChannel(this PythChannels preferred,
		PythChannels minimum)
	{
		if (preferred == PythChannels.Unknown || minimum == PythChannels.Unknown)
			throw new ArgumentOutOfRangeException(nameof(preferred),
				"Pyth channel is unknown.");
		return ChannelRank(preferred) >= ChannelRank(minimum)
			? preferred
			: minimum;
	}

	private static int ChannelRank(PythChannels value)
		=> value switch
		{
			PythChannels.RealTime => 0,
			PythChannels.FixedRate50Milliseconds => 50,
			PythChannels.FixedRate200Milliseconds => 200,
			PythChannels.FixedRate1000Milliseconds => 1000,
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Pyth channel is unknown."),
		};

	public static string NormalizePythSymbol(string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.Length > 512 || value.Any(char.IsControl))
			throw new ArgumentException("Pyth symbol is invalid.", name);
		return value;
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind switch
		{
			DateTimeKind.Utc => value,
			DateTimeKind.Local => value.ToUniversalTime(),
			_ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
		};

	public static long ToUnixSeconds(this DateTime value)
		=> checked((value.EnsureUtc() - DateTime.UnixEpoch).Ticks /
			TimeSpan.TicksPerSecond);

	public static DateTime FromUnixSeconds(this long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddSeconds(value);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"Pyth returned an invalid Unix timestamp.", error);
		}
	}

	public static DateTime FromUnixMicroseconds(this long value)
	{
		try
		{
			return DateTime.UnixEpoch.AddTicks(checked(value * 10));
		}
		catch (Exception error) when (error is OverflowException or
			ArgumentOutOfRangeException)
		{
			throw new InvalidDataException(
				"Pyth returned an invalid microsecond timestamp.", error);
		}
	}

	public static DateTime? ParsePythExpiration(this string value)
	{
		if (value.IsEmpty())
			return null;
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal |
			DateTimeStyles.AdjustToUniversal, out var result))
			throw new InvalidDataException(
				"Pyth returned an invalid expiration timestamp.");
		return result.EnsureUtc();
	}

	public static decimal ScalePythValue(string value, short exponent,
		string name)
	{
		if (!decimal.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var mantissa))
			throw new InvalidDataException($"Pyth returned invalid '{name}'.");
		if (exponent is < -28 or > 28)
			throw new InvalidDataException(
				$"Pyth returned unsupported '{name}' exponent {exponent}.");
		var factor = 1m;
		for (var index = 0; index < Math.Abs(exponent); index++)
			factor = checked(factor * 10m);
		try
		{
			return exponent < 0
				? mantissa / factor
				: checked(mantissa * factor);
		}
		catch (OverflowException error)
		{
			throw new InvalidDataException(
				$"Pyth '{name}' is outside the decimal range.", error);
		}
	}

	public static decimal ToPriceStep(this short exponent)
		=> ScalePythValue("1", exponent, "exponent");

	public static CurrencyTypes? ToCurrency(this string value)
		=> !value.IsEmpty() && Enum.TryParse<CurrencyTypes>(value, true,
			out var currency)
			? currency
			: null;

	public static SecurityTypes ToSecurityType(this PythSymbol value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.InstrumentType is PythInstrumentTypes.Future or
			PythInstrumentTypes.Perpetual)
			return SecurityTypes.Future;
		if (value.InstrumentType == PythInstrumentTypes.Nav ||
			value.AssetType == PythAssetTypes.Nav)
			return SecurityTypes.Fund;
		if (value.InstrumentType is PythInstrumentTypes.Index or
			PythInstrumentTypes.Rate || value.AssetType is PythAssetTypes.Rates or
			PythAssetTypes.InterestRate or PythAssetTypes.Kalshi or
			PythAssetTypes.CryptoIndex or PythAssetTypes.CryptoRedemptionRate)
			return SecurityTypes.Index;
		return value.AssetType switch
		{
			PythAssetTypes.Crypto => SecurityTypes.CryptoCurrency,
			PythAssetTypes.Equity => SecurityTypes.Stock,
			PythAssetTypes.Fx => SecurityTypes.Currency,
			PythAssetTypes.Commodity or PythAssetTypes.Metal =>
				SecurityTypes.Commodity,
			_ => SecurityTypes.Index,
		};
	}
}
