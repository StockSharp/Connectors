namespace StockSharp.Osmosis.Native;

static class OsmosisExtensions
{
	public const string ChainId = "osmosis-1";
	public const string NativeDenomination = "uosmo";

	public static string NormalizeOsmosisAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var decoded = OsmosisSigner.DecodeAddress(value);
		if (!decoded.Prefix.Equals("osmo", StringComparison.Ordinal))
			throw new FormatException(
				$"Osmosis address '{value}' must use the osmo prefix.");
		if (decoded.Data.Length != 20)
			throw new FormatException(
				$"Osmosis address '{value}' has an invalid payload length.");
		return value.ToLowerInvariant();
	}

	public static string NormalizeTransactionHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new FormatException(
				$"Invalid Osmosis transaction hash '{value}'.");
		return value.ToUpperInvariant();
	}

	public static string NormalizeDenomination(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 256 || value.Any(char.IsWhiteSpace) ||
			value.Any(char.IsControl))
			throw new FormatException($"Invalid Osmosis denomination '{value}'.");
		return value;
	}

	public static string NormalizeSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 24 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException($"Invalid Osmosis symbol '{value}'.");
		return value;
	}

	public static string NormalizeSecurityCode(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();
		if (value.Length > 80 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not ('.' or '_' or '-')))
			throw new FormatException(
				$"Invalid Osmosis security code '{value}'.");
		return value;
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
	{
		if (value <= 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scale = DecimalScale(decimals);
		if (value > decimal.MaxValue / scale)
			throw new OverflowException("Osmosis amount is too large.");
		var result = new BigInteger(decimal.Round(value * scale, 0,
			MidpointRounding.AwayFromZero));
		if (result <= 0)
			throw new InvalidOperationException(
				"Osmosis amount rounds to zero base units.");
		return result;
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (value < 0 || value > (BigInteger)decimal.MaxValue)
			throw new OverflowException("Osmosis amount is outside decimal range.");
		return (decimal)value / DecimalScale(decimals);
	}

	public static BigInteger ParseAmount(this string value, string field,
		bool isPositiveRequired = false)
	{
		if (!BigInteger.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result) || result < 0 ||
			isPositiveRequired && result == 0)
			throw new InvalidDataException(
				$"Osmosis returned invalid {field} '{value}'.");
		return result;
	}

	public static ulong ParseUnsigned(this string value, string field)
	{
		if (!ulong.TryParse(value, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Osmosis returned invalid {field} '{value}'.");
		return result;
	}

	public static decimal ParseDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result) || result < 0)
			throw new InvalidDataException(
				$"Osmosis returned invalid {field} '{value}'.");
		return result;
	}

	public static DateTime ParseUtcTime(this string value, string field)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException(
				$"Osmosis returned invalid {field} '{value}'.");
		return DateTime.SpecifyKind(result, DateTimeKind.Utc);
	}

	public static OsmosisCoin ParseCoin(this string value, string field)
	{
		value = value.ThrowIfEmpty(field).Trim();
		var index = 0;
		while (index < value.Length && char.IsAsciiDigit(value[index]))
			index++;
		if (index == 0 || index == value.Length)
			throw new InvalidDataException(
				$"Osmosis returned invalid {field} '{value}'.");
		return new()
		{
			Amount = value[..index].ParseAmount(field, true),
			Denomination = value[index..].NormalizeDenomination(),
		};
	}

	public static SecurityId ToStockSharp(this OsmosisMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Osmosis,
		};
	}

	public static CurrencyTypes? ToCurrency(this string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" => CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			"CNY" => CurrencyTypes.CNY,
			_ => null,
		};

	public static decimal DecimalScale(int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scale = 1m;
		for (var index = 0; index < decimals; index++)
			scale *= 10m;
		return scale;
	}
}
