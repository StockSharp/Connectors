namespace StockSharp.Pendle.Native;

static class PendleExtensions
{
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string ApiNativeTokenAddress =
		NativeTokenAddress;
	public const string ProbeAddress =
		"0x000000000000000000000000000000000000dead";

	public static readonly string TransferTopic = AbiTopic(
		"Transfer(address,address,uint256)");

	public static string GetDefaultRpcEndpoint(this PendleChains chain)
		=> chain switch
		{
			PendleChains.Ethereum =>
				"https://ethereum-rpc.publicnode.com",
			PendleChains.Optimism => "https://mainnet.optimism.io",
			PendleChains.Bnb => "https://bsc-dataseed.binance.org",
			PendleChains.Monad => "https://rpc.monad.xyz",
			PendleChains.HyperEvm =>
				"https://rpc.hyperliquid.xyz/evm",
			PendleChains.Mantle => "https://rpc.mantle.xyz",
			PendleChains.Base => "https://mainnet.base.org",
			PendleChains.Plume => "https://rpc.plume.org",
			PendleChains.Arbitrum => "https://arb1.arbitrum.io/rpc",
			PendleChains.Sonic => "https://rpc.soniclabs.com",
			PendleChains.Berachain => "https://rpc.berachain.com",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Pendle chain."),
		};

	public static string GetNativeSymbol(this PendleChains chain)
		=> chain switch
		{
			PendleChains.Ethereum or PendleChains.Optimism or
				PendleChains.Base or PendleChains.Arbitrum or
				PendleChains.Mantle => "ETH",
			PendleChains.Bnb => "BNB",
			PendleChains.Monad => "MON",
			PendleChains.HyperEvm => "HYPE",
			PendleChains.Plume => "PLUME",
			PendleChains.Sonic => "S",
			PendleChains.Berachain => "BERA",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Pendle chain."),
		};

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

	public static string StripChainPrefix(this string value,
		PendleChains chain)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var separator = value.IndexOf('-');
		if (separator >= 0)
		{
			if (!int.TryParse(value[..separator], NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var chainId) ||
				chainId != (int)chain)
				throw new InvalidDataException(
					$"Pendle asset id '{value}' belongs to another chain.");
			value = value[(separator + 1)..];
		}
		return value.NormalizeAddress();
	}

	public static string NormalizeTokenSymbol(this string value,
		string address)
	{
		address = address.NormalizeAddress();
		value = value?.Trim();
		if (!value.IsEmpty() && value.Length <= 64 && value.All(static ch =>
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

	public static SecurityId ToStockSharp(this PendleSecurity security)
		=> new()
		{
			SecurityCode = security.SecurityCode,
			BoardCode = BoardCodes.Pendle,
		};

	public static string NormalizeSecurityCode(this string value,
		string fallback)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			value = fallback.ThrowIfEmpty(nameof(fallback));
		var normalized = new string(value.Select(static ch =>
			char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-'
				? char.ToUpperInvariant(ch)
				: '-').ToArray());
		while (normalized.Contains("--", StringComparison.Ordinal))
			normalized = normalized.Replace("--", "-",
				StringComparison.Ordinal);
		normalized = normalized.Trim('-');
		return normalized.IsEmpty()
			? fallback.ToUpperInvariant()
			: normalized.Truncate(64, string.Empty);
	}

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

		// JSON-RPC rejects a quantity with leading zeros, and BigInteger.ToString("x")
		// adds one whenever the leading digit is >= 8
		var hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');

		return "0x" + (hex.Length == 0 ? "0" : hex);
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

	public static BigInteger ToBaseUnitsCeiling(this decimal value,
		int decimals)
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
		var isRounded = fraction.Length > decimals &&
			fraction[decimals..].Any(static ch => ch != '0');
		if (fraction.Length > decimals)
			fraction = fraction[..decimals];
		fraction = fraction.PadRight(decimals, '0');
		var digits = (whole + fraction).TrimStart('0');
		var result = digits.IsEmpty()
			? BigInteger.Zero
			: BigInteger.Parse(digits, NumberStyles.Integer,
				CultureInfo.InvariantCulture);
		return isRounded ? result + BigInteger.One : result;
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

	public static DateTime ToUtcTime(this BigInteger seconds)
	{
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.");
		try
		{
			return DateTime.UnixEpoch.AddSeconds((long)seconds);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.", error);
		}
	}

	public static CurrencyTypes? ToCurrency(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" or "WXDAI" =>
				CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"BTC" or "WBTC" => CurrencyTypes.BTC,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency)
				? currency
				: null,
		};
	}

	public static string NormalizeHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length != 66 || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
				static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				$"Invalid EVM transaction hash '{value}'.");
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string NormalizeData(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			value.Length <= 2 || (value.Length - 2) % 2 != 0 ||
			value.Skip(2).Any(static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException("Invalid EVM transaction calldata.");
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string AbiSelector(string signature)
		=> new Sha3Keccack().CalculateHash(signature)[..8];

	public static string AbiTopic(string signature)
		=> "0x" + new Sha3Keccack().CalculateHash(signature);

	public static string AbiWord(BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));

		// BigInteger.ToString("x") prefixes a zero nibble when the leading digit is >= 8 to
		// keep the value unsigned, which would overflow the fixed width word
		var hex = value.ToString("x", CultureInfo.InvariantCulture).TrimStart('0');

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

	public static string ReadTopicAddress(string topic)
	{
		topic = topic.ThrowIfEmpty(nameof(topic)).Trim();
		if (topic.Length != 66 || !topic.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || topic.Skip(2).Any(
				static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException("Invalid indexed EVM address topic.");
		return ("0x" + topic[^40..]).NormalizeAddress();
	}
}
