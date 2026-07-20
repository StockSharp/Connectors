namespace StockSharp.FluidDex.Native;

static class FluidDexExtensions
{
	public const string NativeTokenAddress =
		"0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE";

	public static readonly string SwapTopic = AbiTopic(
		"Swap(bool,uint256,uint256,address)");

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

	public static int GetChainId(this FluidDexChains chain)
		=> chain switch
		{
			FluidDexChains.Ethereum => 1,
			FluidDexChains.BnbSmartChain => 56,
			FluidDexChains.Polygon => 137,
			FluidDexChains.Base => 8453,
			FluidDexChains.Plasma => 9745,
			FluidDexChains.Arbitrum => 42161,
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Fluid DEX chain."),
		};

	public static string GetDefaultRpcEndpoint(this FluidDexChains chain)
		=> chain switch
		{
			FluidDexChains.Ethereum =>
				"https://ethereum-rpc.publicnode.com",
			FluidDexChains.BnbSmartChain =>
				"https://bsc-dataseed.bnbchain.org",
			FluidDexChains.Polygon => "https://polygon-rpc.com",
			FluidDexChains.Base => "https://mainnet.base.org",
			FluidDexChains.Plasma => "https://rpc.plasma.to",
			FluidDexChains.Arbitrum => "https://arb1.arbitrum.io/rpc",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Fluid DEX chain."),
		};

	public static string GetDefaultWebSocketEndpoint(
		this FluidDexChains chain)
		=> chain switch
		{
			FluidDexChains.Ethereum =>
				"wss://ethereum-rpc.publicnode.com",
			FluidDexChains.BnbSmartChain =>
				"wss://bsc-rpc.publicnode.com",
			FluidDexChains.Polygon =>
				"wss://polygon-bor-rpc.publicnode.com",
			FluidDexChains.Base => "wss://mainnet.base.org",
			FluidDexChains.Plasma => null,
			FluidDexChains.Arbitrum => "wss://arb1.arbitrum.io/ws",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Fluid DEX chain."),
		};

	public static (string Symbol, string Name) GetNativeToken(
		this FluidDexChains chain)
		=> chain switch
		{
			FluidDexChains.BnbSmartChain => ("BNB", "BNB"),
			FluidDexChains.Polygon => ("POL", "POL"),
			FluidDexChains.Plasma => ("XPL", "Plasma"),
			FluidDexChains.Ethereum or FluidDexChains.Base or
				FluidDexChains.Arbitrum => ("ETH", "Ether"),
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Fluid DEX chain."),
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

	public static SecurityId ToStockSharp(this FluidDexMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.FluidDex,
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

	public static DateTime ToUtcTime(this BigInteger seconds)
	{
		if (seconds < 0 || seconds > long.MaxValue)
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.");
		return ((long)seconds).FromUnix();
	}

	public static long ToUnixSeconds(this DateTime value)
		=> (long)Math.Floor(value.ToUniversalTime().ToUnix());

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

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

	public static string AbiBoolean(bool value)
		=> AbiWord(value ? BigInteger.One : BigInteger.Zero);

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

	public static string ReadAbiAddress(string value, int index)
	{
		var word = ReadAbiWord(value, index);
		return ("0x" + word.ToString("x", CultureInfo.InvariantCulture)
			.PadLeft(40, '0')).NormalizeAddress();
	}
}
