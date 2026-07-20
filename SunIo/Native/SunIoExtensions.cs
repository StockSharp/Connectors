namespace StockSharp.SunIo.Native;

static class SunIoExtensions
{
	public const string NativeTrxAddress =
		"T9yD14Nj9j7xAB4dbGeiX9h8unkKHxuWwb";
	public const string WrappedTrxAddress =
		"TNUC9Qb1rRpS5CbWLmNMxXBjyFoydXjWFR";
	public const string SwapFunctionSignature =
		"swapExactInput(address[],string[],uint256[],uint24[]," +
		"(uint256,uint256,address,uint256))";
	public const string TriggerContractTypeUrl =
		"type.googleapis.com/protocol.TriggerSmartContract";
	public const string RouteTypes =
		"SUNSWAP_V1,SUNSWAP_V2,SUNSWAP_V3,PSM,CURVE";
	public const decimal TrxScale = 1_000_000m;

	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(4),
		TimeSpan.FromDays(1),
	];

	public static string NormalizeTronAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		_ = SunIoSigner.DecodeAddress(value);
		return value;
	}

	public static bool IsTrx(this string value)
		=> value?.Equals(NativeTrxAddress,
			StringComparison.Ordinal) == true;

	public static string NormalizeTransactionHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch =>
			!Uri.IsHexDigit(ch)))
			throw new FormatException(
				$"Invalid TRON transaction hash '{value}'.");
		return value.ToLowerInvariant();
	}

	public static string NormalizeSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid SUN.io security code '{value}'.");
		return value;
	}

	public static BigInteger ToRawAmount(this decimal value, int decimals)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var scale = GetScale(decimals);
		if (value > decimal.MaxValue / scale)
			throw new OverflowException(
				"SUN.io amount exceeds the supported decimal range.");
		var amount = new BigInteger(decimal.Round(value * scale, 0,
			MidpointRounding.AwayFromZero));
		if (amount <= 0)
			throw new InvalidOperationException(
				"SUN.io amount rounds to zero token units.");
		return amount;
	}

	public static decimal FromRawAmount(this string value, int decimals,
		string field)
	{
		var amount = ParseInteger(value, field);
		if (amount > (BigInteger)decimal.MaxValue)
			throw new OverflowException(
				$"SUN.io {field} exceeds the supported decimal range.");
		return (decimal)amount / GetScale(decimals);
	}

	public static decimal ParseDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ||
			decimal.IsNegative(result))
			throw new InvalidDataException(
				$"SUN.io returned invalid {field} '{value}'.");
		return result;
	}

	public static BigInteger ParseInteger(this string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				$"SUN.io returned invalid {field} '{value}'.");
		return result;
	}

	public static DateTime ParseApiTime(this string value)
	{
		if (!DateTime.TryParseExact(value,
			["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"],
			CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			throw new InvalidDataException(
				$"SUN.io returned invalid transaction time '{value}'.");
		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static DateTime FromUnixMilliseconds(long value)
	{
		if (value < 0 || value >
			(DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) /
			TimeSpan.TicksPerMillisecond)
			throw new InvalidDataException(
				$"TRON returned invalid Unix timestamp '{value}'.");
		return DateTime.UnixEpoch.AddMilliseconds(value);
	}

	public static string ToWire(this SunIoPoolVersions value)
		=> value switch
		{
			SunIoPoolVersions.V1 => "v1",
			SunIoPoolVersions.V2 => "v2",
			SunIoPoolVersions.V3 => "v3",
			SunIoPoolVersions.Usdt20Psm => "usdt20psm",
			SunIoPoolVersions.Usdd202Pool => "usdd202pool",
			SunIoPoolVersions.TwoPoolTusdUsdt => "2pooltusdusdt",
			SunIoPoolVersions.UsdcTwoPoolTusdUsdt =>
				"usdc2pooltusdusdt",
			SunIoPoolVersions.UsddTwoPoolTusdUsdt =>
				"usdd2pooltusdusdt",
			SunIoPoolVersions.UsdjTwoPoolTusdUsdt =>
				"usdj2pooltusdusdt",
			SunIoPoolVersions.OldUsdcPool => "oldusdcpool",
			SunIoPoolVersions.OldThreePool => "old3pool",
			_ => throw new ArgumentOutOfRangeException(nameof(value), value,
				"Unsupported SUN.io pool version."),
		};

	public static SecurityId ToStockSharp(this SunIoMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.SunIo,
		};
	}

	public static CurrencyTypes? ToCurrency(this string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "USDD" or "USDJ" or "TUSD" =>
				CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			"CNY" => CurrencyTypes.CNY,
			_ => null,
		};

	public static DateTime FloorTime(DateTime value, TimeSpan interval)
	{
		if (interval <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(interval));
		value = value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime();
		return new(value.Ticks - value.Ticks % interval.Ticks,
			DateTimeKind.Utc);
	}

	private static decimal GetScale(int decimals)
	{
		if (decimals is < 0 or > 18)
			throw new ArgumentOutOfRangeException(nameof(decimals), decimals,
				"SUN.io supports token precision from zero through 18.");
		var scale = 1m;
		for (var index = 0; index < decimals; index++)
			scale *= 10m;
		return scale;
	}
}
