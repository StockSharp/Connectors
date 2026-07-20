namespace StockSharp.THORChain.Native;

static class THORChainExtensions
{
	public const string RuneAsset = "THOR.RUNE";
	public const string RuneDenomination = "rune";
	public const decimal ProtocolScale = 100_000_000m;

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

	public static string NormalizeThorAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var decoded = THORChainSigner.DecodeAddress(value);
		if (!decoded.Prefix.Equals("thor", StringComparison.Ordinal))
			throw new FormatException(
				$"THORChain address '{value}' must use the thor prefix.");
		if (decoded.Data.Length != 20)
			throw new FormatException(
				$"THORChain address '{value}' has an invalid payload length.");
		return value.ToLowerInvariant();
	}

	public static string NormalizeTransactionHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch =>
			!Uri.IsHexDigit(ch)))
			throw new FormatException(
				$"Invalid THORChain transaction hash '{value}'.");
		return value.ToUpperInvariant();
	}

	public static (string Chain, string Symbol, string Ticker) ParseAsset(
		this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		var separator = value.IndexOf('.');
		if (separator is <= 0 || separator == value.Length - 1 ||
			value.IndexOfAny(['/', '~'], separator + 1) >= 0)
			throw new FormatException(
				$"'{value}' is not a THORChain Layer-1 asset notation.");
		var chain = value[..separator];
		var symbol = value[(separator + 1)..];
		var contractSeparator = symbol.IndexOf('-');
		var ticker = contractSeparator > 0
			? symbol[..contractSeparator]
			: symbol;
		if (!IsAssetPart(chain) || !IsAssetPart(ticker) ||
			symbol.Any(static ch => !char.IsLetterOrDigit(ch) &&
				ch is not ('-' or '_')))
			throw new FormatException(
				$"Invalid THORChain asset notation '{value}'.");
		return (chain, symbol, ticker);
	}

	private static bool IsAssetPart(string value)
		=> !value.IsEmpty() && value.Length <= 64 && value.All(static ch =>
			char.IsLetterOrDigit(ch) || ch == '_');

	public static string NormalizeSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid THORChain security code '{value}'.");
		return value;
	}

	public static BigInteger ToProtocolAmount(this decimal value)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (value > decimal.MaxValue / ProtocolScale)
			throw new OverflowException(
				"THORChain amount exceeds the supported decimal range.");
		var amount = new BigInteger(decimal.Round(value * ProtocolScale, 0,
			MidpointRounding.AwayFromZero));
		if (amount <= 0)
			throw new InvalidOperationException(
				"THORChain amount rounds to zero protocol units.");
		return amount;
	}

	public static decimal FromProtocolAmount(this string value, string field)
	{
		var amount = ParseInteger(value, field);
		if (amount > (BigInteger)decimal.MaxValue)
			throw new OverflowException(
				$"THORChain {field} exceeds the supported decimal range.");
		return (decimal)amount / ProtocolScale;
	}

	public static BigInteger ParseInteger(this string value, string field)
	{
		if (!BigInteger.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				$"THORChain returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal ParseDecimal(string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) ||
			decimal.IsNegative(result))
			throw new InvalidDataException(
				$"THORChain returned invalid {field} '{value}'.");
		return result;
	}

	public static DateTime ParseActionTime(this string value)
	{
		var nanoseconds = ParseInteger(value, "action time");
		var ticks = nanoseconds / 100;
		if (ticks > DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks)
			throw new InvalidDataException(
				$"THORChain returned out-of-range action time '{value}'.");
		return DateTime.UnixEpoch.AddTicks((long)ticks);
	}

	public static SecurityId ToStockSharp(this THORChainMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.THORChain,
		};
	}

	public static CurrencyTypes? ToCurrency(this string ticker)
		=> ticker?.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" => CurrencyTypes.USD,
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
}
