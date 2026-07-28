namespace StockSharp.Velodrome.Native;

static class VelodromeExtensions
{
	public const int ChainId = 10;
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string ClassicFactoryAddress =
		"0xF1046053aa5682b4F9a81b5481394DA16BE5FF5a";
	public const string ClassicRouterAddress =
		"0xa062aE8A9c5e11aaA026fc2670B0D65cCc8B2858";
	public const string InitialSlipstreamFactoryAddress =
		"0xCc0bDDB707055e04e497aB22a59c2aF4391cd12F";
	public const string InitialSlipstreamQuoterAddress =
		"0x89D8218ed5fF1e46d8dcd33fb0bbeE3be1621466";
	public const string InitialSlipstreamRouterAddress =
		"0x0792a633F0c19c351081CF4B211F68F79bCc9676";
	public const string GaugeCapsFactoryAddress =
		"0xe13Dd1fbA721Aa81a1826D9523AC9BC7d260c879";
	public const string GaugeCapsQuoterAddress =
		"0xAd432b2ca49965266133F2bd4c17dc1Ec12f5DEB";
	public const string GaugeCapsRouterAddress =
		"0xbA3aEe516399388C779463183d00bB579f5041Ca";
	public const string GaugesV3FactoryAddress =
		"0xe13Dd1fbA721Aa81a1826D9523AC9BC7d260c879";
	public const string GaugesV3QuoterAddress =
		"0xAd432b2ca49965266133F2bd4c17dc1Ec12f5DEB";
	public const string GaugesV3RouterAddress =
		"0xbA3aEe516399388C779463183d00bB579f5041Ca";

	public static readonly string ClassicSwapTopic = AbiTopic(
		"Swap(address,address,uint256,uint256,uint256,uint256)");
	public static readonly string SlipstreamSwapTopic = AbiTopic(
		"Swap(address,address,int256,int256,uint160,uint128,int24)");

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

	public static SecurityId ToStockSharp(this VelodromeMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Velodrome,
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
		return ((long)seconds).FromUnix();
	}

	public static long ToUnixSeconds(this DateTime value)
		=> (long)Math.Floor(value.ToUniversalTime().ToUnix());

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var currency)
			? currency
			: null;

	public static bool IsClassicFactory(this string address)
		=> address.NormalizeAddress().EqualsIgnoreCase(
			ClassicFactoryAddress.NormalizeAddress());

	public static bool TryGetSlipstreamDeployment(this string factory,
		out string router, out string quoter)
	{
		factory = factory.NormalizeAddress();
		if (factory.EqualsIgnoreCase(
			InitialSlipstreamFactoryAddress.NormalizeAddress()))
		{
			router = InitialSlipstreamRouterAddress.NormalizeAddress();
			quoter = InitialSlipstreamQuoterAddress.NormalizeAddress();
			return true;
		}
		if (factory.EqualsIgnoreCase(
			GaugeCapsFactoryAddress.NormalizeAddress()))
		{
			router = GaugeCapsRouterAddress.NormalizeAddress();
			quoter = GaugeCapsQuoterAddress.NormalizeAddress();
			return true;
		}
		if (factory.EqualsIgnoreCase(
			GaugesV3FactoryAddress.NormalizeAddress()))
		{
			router = GaugesV3RouterAddress.NormalizeAddress();
			quoter = GaugesV3QuoterAddress.NormalizeAddress();
			return true;
		}
		router = null;
		quoter = null;
		return false;
	}

	public static string GetSwapTopic(this VelodromePoolTypes poolType)
		=> poolType == VelodromePoolTypes.Slipstream
			? SlipstreamSwapTopic
			: ClassicSwapTopic;

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

		// BigInteger.ToString("x") prefixes a zero nibble when the leading digit is >= 8 to
		// keep the value unsigned, and an address is the low 20 bytes of the word anyway
		var hex = word.ToString("x", CultureInfo.InvariantCulture);

		return ("0x" + (hex.Length > 40 ? hex[^40..] : hex.PadLeft(40, '0')))
			.NormalizeAddress();
	}
}
