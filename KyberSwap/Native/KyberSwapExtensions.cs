namespace StockSharp.KyberSwap.Native;

static class KyberSwapExtensions
{
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string ProbeAddress =
		"0x0000000000000000000000000000000000000001";

	public static readonly string TransferTopic = AbiTopic(
		"Transfer(address,address,uint256)");

	public static string GetApiName(this KyberSwapChains chain)
		=> chain switch
		{
			KyberSwapChains.Ethereum => "ethereum",
			KyberSwapChains.Optimism => "optimism",
			KyberSwapChains.Bnb => "bsc",
			KyberSwapChains.Polygon => "polygon",
			KyberSwapChains.Base => "base",
			KyberSwapChains.Arbitrum => "arbitrum",
			KyberSwapChains.Avalanche => "avalanche",
			KyberSwapChains.Linea => "linea",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported KyberSwap chain."),
		};

	public static string GetDefaultRpcEndpoint(this KyberSwapChains chain)
		=> chain switch
		{
			KyberSwapChains.Ethereum =>
				"https://ethereum-rpc.publicnode.com",
			KyberSwapChains.Optimism => "https://mainnet.optimism.io",
			KyberSwapChains.Bnb => "https://bsc-dataseed.binance.org",
			KyberSwapChains.Polygon =>
				"https://polygon-bor-rpc.publicnode.com",
			KyberSwapChains.Base => "https://mainnet.base.org",
			KyberSwapChains.Arbitrum => "https://arb1.arbitrum.io/rpc",
			KyberSwapChains.Avalanche =>
				"https://api.avax.network/ext/bc/C/rpc",
			KyberSwapChains.Linea => "https://rpc.linea.build",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported KyberSwap chain."),
		};

	public static string GetDefaultMarkets(this KyberSwapChains chain)
		=> chain switch
		{
			KyberSwapChains.Ethereum =>
				"0xC02aaA39b223FE8D0A0e5C4F27eAD9083C756Cc2|" +
				"0xA0b86991c6218b36c1d19d4a2e9eb0ce3606eb48|" +
				"WETH-USDC",
			KyberSwapChains.Optimism =>
				"0x4200000000000000000000000000000000000006|" +
				"0x0b2C639c533813f4Aa9D7837CAF62653d097Ff85|" +
				"WETH-USDC",
			KyberSwapChains.Bnb =>
				"0xbb4CdB9CBd36B01bD1cBaEBF2De08d9173bc095c|" +
				"0x55d398326f99059fF775485246999027B3197955|" +
				"WBNB-USDT",
			KyberSwapChains.Polygon =>
				"0x0d500B1d8E8eD2A4Ccb1A27233D5440B9DADf1270|" +
				"0x3c499c542cef5e3811e1192ce70d8cc03d5c3359|" +
				"WPOL-USDC",
			KyberSwapChains.Base =>
				"0x4200000000000000000000000000000000000006|" +
				"0x833589fcd6edb6e08f4c7c32d4f71b54bda02913|" +
				"WETH-USDC",
			KyberSwapChains.Arbitrum =>
				"0x82af49447d8a07e3bd95bd0d56f35241523fbab1|" +
				"0xaf88d065e77c8cc2239327c5edb3a432268e5831|" +
				"WETH-USDC",
			KyberSwapChains.Avalanche =>
				"0xb31f66aa3c1e785363f0875a1b74e27b85fd66c7|" +
				"0xb97ef9ef8734c71904d8002f8b6bc66dd9c48a6e|" +
				"WAVAX-USDC",
			KyberSwapChains.Linea =>
				"0xe5D7C2a44f73f6b2954A36cF8D8B76Dd5fb8fF|" +
				"0x176211869cA2b568f2A7D4EE941E073a821EE1ff|" +
				"WETH-USDC",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported KyberSwap chain."),
		};

	public static string GetNativeSymbol(this KyberSwapChains chain)
		=> chain switch
		{
			KyberSwapChains.Ethereum or KyberSwapChains.Optimism or
				KyberSwapChains.Base or KyberSwapChains.Arbitrum or
				KyberSwapChains.Linea => "ETH",
			KyberSwapChains.Bnb => "BNB",
			KyberSwapChains.Polygon => "POL",
			KyberSwapChains.Avalanche => "AVAX",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported KyberSwap chain."),
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

	public static SecurityId ToStockSharp(this KyberSwapMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.KyberSwap,
		};

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
