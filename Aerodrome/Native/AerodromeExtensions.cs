namespace StockSharp.Aerodrome.Native;

static class AerodromeExtensions
{
	public const int ChainId = 8453;
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string ClassicFactoryAddress =
		"0x420DD381b31aEf6683db6B902084cB0FFECe40Da";
	public const string ClassicRouterAddress =
		"0xcF77a3Ba9A5CA399B7c97c74d54e5b1Beb874E43";
	public const string InitialSlipstreamFactoryAddress =
		"0x5e7BB104d84c7CB9B682AaC2F3d509f5F406809A";
	public const string InitialSlipstreamQuoterAddress =
		"0x254cF9E1E6e233aa1AC962CB9B05b2cfeAaE15b0";
	public const string InitialSlipstreamRouterAddress =
		"0xBE6D8f0d05cC4be24d5167a3eF062215bE6D18a5";
	public const string GaugeCapsFactoryAddress =
		"0xaDe65c38CD4849aDBA595a4323a8C7DdfE89716a";
	public const string GaugeCapsQuoterAddress =
		"0x3d4C22254F86f64B7eC90ab8F7aeC1FBFD271c6C";
	public const string GaugeCapsRouterAddress =
		"0xcbBb8035cAc7D4B3Ca7aBb74cF7BdF900215Ce0D";
	public const string GaugesV3FactoryAddress =
		"0xf8f2eB4940CFE7d13603DDDD87f123820Fc061Ef";
	public const string GaugesV3QuoterAddress =
		"0x514c8B5f54112481E28028F1166Bd78501089259";
	public const string GaugesV3RouterAddress =
		"0x698Cb2b6dd822994581fEa6eA4Fc755d1363A92F";

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

	public static SecurityId ToStockSharp(this AerodromeMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Aerodrome,
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

	public static string GetSwapTopic(this AerodromePoolTypes poolType)
		=> poolType == AerodromePoolTypes.Slipstream
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
