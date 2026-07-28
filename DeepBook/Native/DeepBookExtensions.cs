namespace StockSharp.DeepBook.Native;

static class DeepBookExtensions
{
	public const string SuiCoinType =
		"0x0000000000000000000000000000000000000000000000000000000000000002" +
		"::sui::SUI";
	public const string DeepCoinType =
		"0xdeeb7a4662eec9f2f3def03fb937a663dddaa2e215b8078a284d026b7946c270" +
		"::deep::DEEP";
	public const string UsdcCoinType =
		"0xdba34672e30cb065b1f93e3ab55318768fd6fef66c15942c9f7cb846e2f900e7" +
		"::usdc::USDC";
	public const string MainnetPackage =
		"0x337f4f4f6567fcd778d5454f27c16c70e2f274cc6377ea6249ddf491482ef497";
	public const string Registry =
		"0xaf16199a2dff736e9f07a845f23c5da6df6f756eddb631aed9d24a93efc4549d";
	public const string Clock =
		"0x0000000000000000000000000000000000000000000000000000000000000006";

	private const string _base58Alphabet =
		"123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
	private static readonly Regex _addressPattern = new(
		@"0x[0-9a-fA-F]+", RegexOptions.Compiled |
		RegexOptions.CultureInvariant);

	public static string NormalizeSuiAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				$"Invalid Sui address '{value}'.", nameof(value));
		var hex = value[2..];
		if (hex.Length is < 1 or > 64 || hex.Any(static ch =>
			!Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid Sui address '{value}'.", nameof(value));
		return "0x" + hex.ToLowerInvariant().PadLeft(64, '0');
	}

	public static byte[] DecodeSuiAddress(this string value)
		=> Convert.FromHexString(value.NormalizeSuiAddress()[2..]);

	public static string NormalizeCoinType(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		value = new string(value.Where(static ch => !char.IsWhiteSpace(ch))
			.ToArray());
		if (!value.Contains("::", StringComparison.Ordinal))
			throw new ArgumentException(
				$"Invalid Sui coin type '{value}'.", nameof(value));
		value = _addressPattern.Replace(value, static match =>
			match.Value.NormalizeSuiAddress());
		if (value.Any(static ch => !(char.IsLetterOrDigit(ch) ||
			ch is 'x' or ':' or '_' or '<' or '>' or ',')))
			throw new ArgumentException(
				$"Invalid Sui coin type '{value}'.", nameof(value));
		return value;
	}

	public static string NormalizeTokenSymbol(this string value,
		string coinType)
	{
		value = value?.Trim();
		if (!value.IsEmpty() && value.Length <= 20 && value.All(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
			return value.ToUpperInvariant();
		var address = coinType.NormalizeCoinType()[2..66];
		return "TOKEN-" + address[..6].ToUpperInvariant();
	}

	public static string NormalizeTokenName(this string value,
		string fallback)
	{
		fallback = fallback.ThrowIfEmpty(nameof(fallback));
		value = value?.Trim();
		if (value.IsEmpty())
			return fallback;
		value = new string(value.Where(static ch => !char.IsControl(ch))
			.ToArray()).Trim();
		return value.IsEmpty()
			? fallback
			: value.Truncate(128, string.Empty);
	}

	public static string NormalizePoolName(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!(char.IsLetterOrDigit(ch) || ch is '_')))
			throw new ArgumentException(
				$"Invalid DeepBook pool name '{value}'.", nameof(value));
		return value;
	}

	public static string NormalizeSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')))
			throw new ArgumentException(
				$"Invalid DeepBook security code '{value}'.", nameof(value));
		return value;
	}

	public static SecurityId ToStockSharp(this DeepBookMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.DeepBook,
		};

	public static ulong ToBaseUnits(this decimal value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scale = Pow10(decimals);
		var scaled = value * scale;
		if (decimal.Truncate(scaled) != scaled)
			throw new InvalidOperationException(
				$"Value '{value}' has more than {decimals} decimals.");
		if (scaled > ulong.MaxValue)
			throw new OverflowException("Sui coin amount exceeds u64.");
		return decimal.ToUInt64(scaled);
	}

	public static ulong ToBaseUnitsRoundedUp(this decimal value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scaled = decimal.Ceiling(value * Pow10(decimals));
		if (scaled > ulong.MaxValue)
			throw new OverflowException("Sui coin amount exceeds u64.");
		return decimal.ToUInt64(scaled);
	}

	public static decimal FromBaseUnits(this ulong value, int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		return value / Pow10(decimals);
	}

	public static ulong ApplyMinimumSlippage(this ulong value,
		int basisPoints)
	{
		if (basisPoints is < 0 or >= 10_000)
			throw new ArgumentOutOfRangeException(nameof(basisPoints));
		return (ulong)((BigInteger)value * (10_000 - basisPoints) / 10_000);
	}

	public static ulong ApplyMaximumSlippage(this ulong value,
		int basisPoints)
	{
		if (basisPoints is < 0 or >= 10_000)
			throw new ArgumentOutOfRangeException(nameof(basisPoints));
		var protectedValue = ((BigInteger)value * (10_000 + basisPoints) +
			9_999) / 10_000;
		if (protectedValue > ulong.MaxValue)
			throw new OverflowException("Protected Sui amount exceeds u64.");
		return (ulong)protectedValue;
	}

	public static DateTime ToUtc(this Timestamp value, DateTime fallback)
	{
		if (value is null)
			return fallback.Kind == DateTimeKind.Utc
				? fallback
				: fallback.ToUniversalTime();
		return DateTime.SpecifyKind(value.ToDateTime(), DateTimeKind.Utc);
	}

	public static string NormalizeTransactionDigest(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length is < 32 or > 64 || value.Any(ch =>
			_base58Alphabet.IndexOf(ch) < 0))
			throw new InvalidDataException(
				$"Invalid Sui transaction digest '{value}'.");
		return value;
	}

	public static CurrencyTypes? ToCurrency(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" or "AUSD" =>
				CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"BTC" or "WBTC" or "XBTC" => CurrencyTypes.BTC,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency)
					? currency
					: null,
		};
	}

	public static string ToDeepBookInterval(this TimeSpan value)
		=> value switch
		{
			{ TotalMinutes: 1 } => "1m",
			{ TotalMinutes: 5 } => "5m",
			{ TotalMinutes: 15 } => "15m",
			{ TotalMinutes: 30 } => "30m",
			{ TotalHours: 1 } => "1h",
			{ TotalHours: 4 } => "4h",
			{ TotalDays: 1 } => "1d",
			{ TotalDays: 7 } => "1w",
			_ => throw new NotSupportedException(
				$"DeepBook does not support '{value}' candles."),
		};

	private static decimal Pow10(int decimals)
	{
		var result = 1m;
		for (var index = 0; index < decimals; index++)
			result *= 10m;
		return result;
	}
}
