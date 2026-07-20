namespace StockSharp.Curve.Native;

static class CurveExtensions
{
	public const int ChainId = 1;
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string CurveNativeTokenAddress =
		"0xEeeeeEeeeEeEeeEeEeEeeEEEeeeeEeeeeeeeEEeE";
	public const string DefaultRouterAddress =
		"0x45312ea0eFf7E09C83CBE249fa1d7598c4C8cd4e";

	public static readonly string StableSwapTopic = AbiTopic(
		"TokenExchange(address,int128,uint256,int128,uint256)");
	public static readonly string StableUnderlyingSwapTopic = AbiTopic(
		"TokenExchangeUnderlying(address,int128,uint256,int128,uint256)");
	public static readonly string CryptoSwapTopic = AbiTopic(
		"TokenExchange(address,uint256,uint256,uint256,uint256)");
	public static readonly string CryptoNgSwapTopic = AbiTopic(
		"TokenExchange(address,uint256,uint256,uint256,uint256,uint256,uint256)");

	public static readonly string[] SwapTopics =
	[
		StableSwapTopic,
		StableUnderlyingSwapTopic,
		CryptoSwapTopic,
		CryptoNgSwapTopic,
	];

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

	public static bool IsNativeToken(this string address)
	{
		if (address.IsEmpty())
			return false;
		address = address.NormalizeAddress();
		return address.EqualsIgnoreCase(NativeTokenAddress) ||
			address.EqualsIgnoreCase(CurveNativeTokenAddress);
	}

	public static bool IsZeroAddress(this string address)
		=> !address.IsEmpty() && address.NormalizeAddress().EqualsIgnoreCase(
			NativeTokenAddress);

	public static bool TryToRegistryType(this string registryId,
		out CurveRegistryTypes registryType)
	{
		switch (registryId?.Trim().ToLowerInvariant())
		{
			case "main":
				registryType = CurveRegistryTypes.Main;
				return true;
			case "factory":
				registryType = CurveRegistryTypes.StableFactory;
				return true;
			case "factory-stable-ng":
				registryType = CurveRegistryTypes.StableNgFactory;
				return true;
			case "factory-crvusd":
				registryType = CurveRegistryTypes.CrvUsdFactory;
				return true;
			case "crypto":
				registryType = CurveRegistryTypes.Crypto;
				return true;
			case "factory-crypto":
				registryType = CurveRegistryTypes.CryptoFactory;
				return true;
			case "factory-twocrypto":
				registryType = CurveRegistryTypes.TwoCryptoFactory;
				return true;
			case "factory-tricrypto":
				registryType = CurveRegistryTypes.TriCryptoFactory;
				return true;
			default:
				registryType = default;
				return false;
		}
	}

	public static CurvePoolTypes ToPoolType(
		this CurveRegistryTypes registryType)
		=> registryType switch
		{
			CurveRegistryTypes.Main or
			CurveRegistryTypes.StableFactory or
			CurveRegistryTypes.StableNgFactory or
			CurveRegistryTypes.CrvUsdFactory => CurvePoolTypes.Stable,
			CurveRegistryTypes.Crypto or
			CurveRegistryTypes.CryptoFactory or
			CurveRegistryTypes.TwoCryptoFactory => CurvePoolTypes.Crypto,
			CurveRegistryTypes.TriCryptoFactory => CurvePoolTypes.Tricrypto,
			_ => throw new ArgumentOutOfRangeException(nameof(registryType),
				registryType, "Unsupported Curve registry type."),
		};

	public static int ToRouterPoolType(this CurvePoolTypes poolType)
		=> poolType switch
		{
			CurvePoolTypes.Stable => 1,
			CurvePoolTypes.Crypto => 2,
			CurvePoolTypes.Tricrypto => 3,
			_ => throw new ArgumentOutOfRangeException(nameof(poolType),
				poolType, "Unsupported Curve router pool type."),
		};

	public static SecurityId ToStockSharp(this CurveMarket market)
		=> new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Curve,
		};

	public static string CreateTradeId(string transactionHash,
		int soldIndex, int boughtIndex)
		=> transactionHash.NormalizeHash() + ":" +
			soldIndex.ToString(CultureInfo.InvariantCulture) + ":" +
			boughtIndex.ToString(CultureInfo.InvariantCulture);

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
		return DateTime.UnixEpoch.AddSeconds((long)seconds);
	}

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
