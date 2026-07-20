namespace StockSharp.QuickSwap.Native;

static class QuickSwapExtensions
{
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";

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

	public static string NormalizeAddress(this string address)
	{
		address = address.ThrowIfEmpty(nameof(address)).Trim();
		if (address.Length != 42 || !address.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || address.Skip(2).Any(
			static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid EVM address '{address}'.", nameof(address));
		return "0x" + address[2..].ToLowerInvariant();
	}

	public static bool IsNativeToken(this string address)
		=> !address.IsEmpty() && address.NormalizeAddress()
			.EqualsIgnoreCase(NativeTokenAddress);

	public static string NormalizeTokenSymbol(this string value,
		string address)
	{
		address = address.NormalizeAddress();
		value = value?.Trim();
		if (!value.IsEmpty() && value.Length <= 20 && value.All(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'))
			return value.ToUpperInvariant();
		return "TOKEN-" + address[2..8].ToUpperInvariant();
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

	public static SecurityId ToStockSharp(this QuickSwapMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.QuickSwap,
		};

	public static decimal? ToDecimalInvariant(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static BigInteger ParseInteger(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var hex = value[2..];
			if (hex.IsEmpty())
				return BigInteger.Zero;
			return BigInteger.Parse("0" + hex,
				NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		}
		return BigInteger.Parse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"JSON-RPC quantities cannot be negative.");
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 255)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var text = value.ToString("0.############################",
			CultureInfo.InvariantCulture);
		var separator = text.IndexOf('.');
		var whole = separator < 0 ? text : text[..separator];
		var fraction = separator < 0 ? string.Empty : text[(separator + 1)..];
		if (fraction.Length > decimals)
		{
			if (fraction[decimals..].Any(static ch => ch != '0'))
				throw new InvalidOperationException(
					$"Value '{value}' has more than {decimals} decimals.");
			fraction = fraction[..decimals];
		}
		fraction = fraction.PadRight(decimals, '0');
		var digits = (whole + fraction).TrimStart('0');
		return digits.IsEmpty() ? BigInteger.Zero : BigInteger.Parse(digits,
			NumberStyles.Integer, CultureInfo.InvariantCulture);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var digits = value.ToString(CultureInfo.InvariantCulture);
		if (decimals > 0)
		{
			digits = digits.PadLeft(decimals + 1, '0');
			digits = digits.Insert(digits.Length - decimals, ".");
		}
		if (!decimal.TryParse(digits, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new OverflowException(
				"Token amount exceeds the supported decimal range.");
		return result;
	}

	public static DateTime ToUtcTime(this string seconds)
	{
		if (!long.TryParse(seconds, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var value) || value < 0 ||
			value > (DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) /
				TimeSpan.TicksPerSecond)
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.");
		return value.FromUnix();
	}

	public static long ToUnixSeconds(this DateTime value)
	{
		value = value.Kind == DateTimeKind.Utc
			? value
			: value.ToUniversalTime();
		return checked((long)Math.Floor(value.ToUnix()));
	}

	public static string NativeSymbol(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon => "POL",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported QuickSwap chain."),
		};

	public static CurrencyTypes? ToCurrency(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDC.E" or "USDT" or "DAI" or
				"USDS" or "USD+" => CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			"CNY" => CurrencyTypes.CNY,
			_ => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
				? currency
				: null,
		};
	}

	public static string GetV2Factory(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon =>
				"0x5757371414417b8C6CAad45bAeF941aBc7d3Ab32",
			_ => throw new NotSupportedException(
				$"QuickSwap v2 is not deployed on {chain}."),
		};

	public static string GetV2Router(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon =>
				"0xa5E0829CaCEd8fFDD4De3c43696c57F7D7A678ff",
			_ => throw new NotSupportedException(
				$"QuickSwap v2 is not deployed on {chain}."),
		};

	public static string GetV3Factory(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon =>
				"0x411b0fAcC3489691f28ad58c47006AF5E3Ab3A28",
			_ => throw new NotSupportedException(
				$"QuickSwap Algebra v3 is not deployed on {chain}."),
		};

	public static string GetV3Router(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon =>
				"0xf5b509bB0909a69B1c207E495f687a596C168E12",
			_ => throw new NotSupportedException(
				$"QuickSwap Algebra v3 is not deployed on {chain}."),
		};

	public static string GetV3Quoter(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon =>
				"0xa15F0D7377B2A0C0c10db057f641beD21028FC89",
			_ => throw new NotSupportedException(
				$"QuickSwap Algebra v3 is not deployed on {chain}."),
		};

	public static string GetDefaultV2Subgraph(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon => null,
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported QuickSwap chain."),
		};

	public static string GetDefaultV3Subgraph(this QuickSwapChains chain)
		=> chain switch
		{
			QuickSwapChains.Polygon => null,
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported QuickSwap chain."),
		};

	public static string AbiSelector(string signature)
		=> new Sha3Keccack().CalculateHash(signature)[..8];

	public static string AbiTopic(string signature)
		=> "0x" + new Sha3Keccack().CalculateHash(signature);

	public static string AbiWord(BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		var hex = value.ToString("x", CultureInfo.InvariantCulture);
		if (hex.Length > 64)
			throw new ArgumentOutOfRangeException(nameof(value),
				"ABI integer exceeds 256 bits.");
		return hex.PadLeft(64, '0');
	}

	public static string AbiAddress(string address)
		=> address.NormalizeAddress()[2..].PadLeft(64, '0');

	public static string EncodeStaticCall(string signature,
		params string[] words)
		=> "0x" + AbiSelector(signature) + string.Concat(words);

	public static BigInteger ReadAbiWord(string value, int index)
	{
		if (value.IsEmpty() || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException("Invalid ABI response.");
		var start = 2 + checked(index * 64);
		if (start < 2 || start + 64 > value.Length)
			throw new InvalidDataException("ABI response is truncated.");
		return BigInteger.Parse("0" + value.Substring(start, 64),
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	}

	public static BigInteger ReadAbiSignedWord(string value, int index)
	{
		var result = ReadAbiWord(value, index);
		return result >= BigInteger.One << 255
			? result - (BigInteger.One << 256)
			: result;
	}

	public static string ReadAbiAddress(string value, int index)
	{
		var word = ReadAbiWord(value, index);
		return ("0x" + word.ToString("x", CultureInfo.InvariantCulture)
			.PadLeft(40, '0')).NormalizeAddress();
	}
}
