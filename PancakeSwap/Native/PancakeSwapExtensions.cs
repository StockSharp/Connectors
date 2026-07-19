namespace StockSharp.PancakeSwap.Native;

static class PancakeSwapExtensions
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

	public static SecurityId ToStockSharp(this PancakeSwapMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.PancakeSwap,
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
			CultureInfo.InvariantCulture, out var value) || value < 0)
			throw new InvalidDataException(
				$"Invalid Unix timestamp '{seconds}'.");
		return value.FromUnix();
	}

	public static long ToUnixSeconds(this DateTime value)
		=> (long)Math.Floor(value.ToUniversalTime().ToUnix());

	public static string NativeSymbol(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.BnbSmartChain => "BNB",
			PancakeSwapChains.BnbSmartChainTestnet => "tBNB",
			PancakeSwapChains.OpBnb => "BNB",
			PancakeSwapChains.Monad => "MON",
			_ => "ETH",
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static string GetV2Factory(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.BnbSmartChain =>
				"0xcA143Ce32Fe78f1f7019d7d551a6402fC5350c73",
			PancakeSwapChains.Ethereum =>
				"0x1097053Fd2ea711dad45caCcc45EfF7548fCB362",
			PancakeSwapChains.ZkSync =>
				"0xd03D8D566183F0086d8D09A84E1e30b58Dd5619d",
			PancakeSwapChains.BnbSmartChainTestnet =>
				"0x6725F303b657a9451d8BA641348b6761A6CC7a17",
			PancakeSwapChains.Arbitrum or PancakeSwapChains.Linea or
			PancakeSwapChains.Base or PancakeSwapChains.OpBnb or
			PancakeSwapChains.Monad or PancakeSwapChains.RobinhoodChain =>
				"0x02a84c1b3BBD7401a5f7fa98a384EBC70bB5749E",
			_ => throw new NotSupportedException(
				$"PancakeSwap v2 is not deployed on {chain}."),
		};

	public static string GetV2Router(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.BnbSmartChain =>
				"0x10ED43C718714eb63d5aA57B78B54704E256024E",
			PancakeSwapChains.Ethereum =>
				"0xEfF92A263d31888d860bD50809A8D171709b7b1c",
			PancakeSwapChains.ZkSync =>
				"0x5aEaF2883FBf30f3D62471154eDa3C0c1b05942d",
			PancakeSwapChains.Monad =>
				"0xB1Bc24c34e88f7D43D5923034E3a14B24DaACfF9",
			PancakeSwapChains.BnbSmartChainTestnet =>
				"0xD99D1c33F9C3444f8101754aBC46c52416550D1",
			PancakeSwapChains.Arbitrum or PancakeSwapChains.Linea or
			PancakeSwapChains.Base or PancakeSwapChains.OpBnb or
			PancakeSwapChains.RobinhoodChain =>
				"0x8cFe327CEc66d1C090Dd72bd0FF11d690C33a2Eb",
			_ => throw new NotSupportedException(
				$"PancakeSwap v2 is not deployed on {chain}."),
		};

	public static string GetV3Factory(this PancakeSwapChains chain)
		=> chain == PancakeSwapChains.ZkSync
			? "0x1BB72E0CbbEA93c08f535fc7856E0338D7F7a8aB"
			: "0x0BFbCF9fa4f9C56B0F40a671Ad40E0805A091865";

	public static string GetV3Router(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.ZkSync =>
				"0xD70C70AD87aa8D45b8D59600342FB3AEe76E3c68",
			PancakeSwapChains.RobinhoodChain =>
				throw new NotSupportedException(
					"PancakeSwap does not publish a direct v3 SwapRouter " +
					"deployment for Robinhood Chain."),
			_ => "0x1b81D678ffb9C0263b24A97847620C99d213eB14",
		};

	public static string GetV3Quoter(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.ZkSync =>
				"0x3d146FcE6c1006857750cBe8aF44f76a28041CCc",
			PancakeSwapChains.BnbSmartChainTestnet =>
				"0xbC203d7f83677c7ed3F7acEc959963E7F4ECC5C2",
			PancakeSwapChains.RobinhoodChain =>
				"0x8553AA1615549A86882151784b329B017aA7c832",
			_ => "0xB048Bbc1Ee6b733FFfCFb9e9CeF7375518e25997",
		};

	public static string GetDefaultV2Subgraph(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.Ethereum =>
				"9opY17WnEPD4REcC43yHycQthSeUMQE26wyoeMjZTLEx",
			PancakeSwapChains.Arbitrum =>
				"EsL7geTRcA3LaLLM9EcMFzYbUgnvf8RixoEEGErrodB3",
			PancakeSwapChains.ZkSync =>
				"6dU6WwEz22YacyzbTbSa3CECCmaD8G7oQ8aw6MYd5VKU",
			PancakeSwapChains.Linea =>
				"Eti2Z5zVEdARnuUzjCbv4qcimTLysAizsqH3s6cBfPjB",
			PancakeSwapChains.Base =>
				"2NjL7L4CmQaGJSacM43ofmH6ARf6gJoBeBaJtz9eWAQ9",
			PancakeSwapChains.OpBnb =>
				"https://opbnb-mainnet-graph.nodereal.io/subgraphs/name/" +
				"pancakeswap/exchange-v2",
			_ => null,
		};

	public static string GetDefaultV3Subgraph(this PancakeSwapChains chain)
		=> chain switch
		{
			PancakeSwapChains.BnbSmartChain =>
				"Hv1GncLY5docZoGtXjo4kwbTvxm3MAhVZqBZE4sUT9eZ",
			PancakeSwapChains.Ethereum =>
				"CJYGNhb7RvnhfBDjqpRnD3oxgyhibzc7fkAMa38YV3oS",
			PancakeSwapChains.Arbitrum =>
				"251MHFNN1rwjErXD2efWMpNS73SANZN8Ua192zw6iXve",
			PancakeSwapChains.ZkSync =>
				"3dKr3tYxTuwiRLkU9vPj3MvZeUmeuGgWURbFC72ZBpYY",
			PancakeSwapChains.Linea =>
				"6gCTVX98K3A9Hf9zjvgEKwjz7rtD4C1V173RYEdbeMFX",
			PancakeSwapChains.Base =>
				"5YYKGBcRkJs6tmDfB3RpHdbK2R5KBACHQebXVgbUcYQp",
			PancakeSwapChains.OpBnb =>
				"https://opbnb-mainnet-graph.nodereal.io/subgraphs/name/" +
				"pancakeswap/exchange-v3",
			_ => null,
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
