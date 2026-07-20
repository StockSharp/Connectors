namespace StockSharp.Avantis.Native;

static class AvantisExtensions
{
	public const int ChainId = 8453;
	public const string NativeTokenAddress =
		"0x0000000000000000000000000000000000000000";
	public const string TradingStorageAddress =
		"0x8a311D7048c35985aa31C131B9A13e03a5f7422d";
	public const string UsdcAddress =
		"0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913";
	public const string TradingAddress =
		"0x44914408af82bC9983bbb330e3578E1105e11d4e";

	public static readonly string OpenLimitPlacedTopic = AbiTopic(
		"OpenLimitPlaced(address,uint256,uint256,bool,uint256,uint256,uint8,uint256,uint256)");
	public static readonly string MarketOrderInitiatedTopic = AbiTopic(
		"MarketOrderInitiated(address,uint256,bool,uint256,uint256,bool,bool,uint256,uint256)");
	public static readonly string OpenLimitCanceledTopic = AbiTopic(
		"OpenLimitCanceled(address,uint256,uint256,uint256,uint256)");

	public static SecurityId ToStockSharp(this AvantisMarket market)
		=> new()
		{
			SecurityCode = market.Symbol,
			BoardCode = BoardCodes.Avantis,
		};

	public static CurrencyTypes? ToAvantisCurrency(this string value)
	{
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USDC" or "USDT" => CurrencyTypes.USD,
			_ => Enum.TryParse<CurrencyTypes>(value, true, out var currency)
				? currency
				: null,
		};
	}

	public static string NormalizeAddress(this string address)
	{
		address = address.ThrowIfEmpty(nameof(address)).Trim();
		if (address.Length != 42 || !address.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || address.Skip(2).Any(
				static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				"Invalid EVM address '" + address + "'.", nameof(address));
		return "0x" + address[2..].ToLowerInvariant();
	}

	public static string NormalizeHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length != 66 || !value.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || value.Skip(2).Any(
				static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				"Invalid EVM transaction hash '" + value + "'.");
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static string NormalizeFeedId(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new InvalidDataException(
				"Invalid Pyth feed identifier '" + value + "'.");
		return value.ToLowerInvariant();
	}

	public static BigInteger ParseInteger(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var hex = value[2..];
			return hex.IsEmpty()
				? BigInteger.Zero
				: BigInteger.Parse("0" + hex,
					NumberStyles.AllowHexSpecifier,
					CultureInfo.InvariantCulture);
		}
		return BigInteger.Parse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
	}

	public static decimal ParseScaled(this string value, int decimals,
		string name)
		=> value.ThrowIfEmpty(name).ParseInteger().FromBaseUnits(decimals);

	public static decimal? TryParseScaled(this string value, int decimals)
	{
		if (value.IsEmpty())
			return null;
		try
		{
			return value.ParseInteger().FromBaseUnits(decimals);
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value), value,
				"JSON-RPC quantities cannot be negative.");
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals,
		string name)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(name, value,
				"Value cannot be negative.");
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var factor = Pow10(decimals);
		var scaled = value * factor;
		if (scaled != decimal.Truncate(scaled))
			throw new ArgumentOutOfRangeException(name, value,
				"Value has more than " + decimals + " decimal places.");
		return new BigInteger(scaled);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		var factor = Pow10(decimals);
		if (value > new BigInteger(decimal.MaxValue) ||
			value < new BigInteger(decimal.MinValue))
			throw new OverflowException(
				"EVM value exceeds the supported decimal range.");
		return (decimal)value / factor;
	}

	public static decimal ApplyExponent(this string value, int exponent,
		string name)
	{
		var integer = value.ThrowIfEmpty(name).ParseInteger();
		if (exponent <= 0)
			return integer.FromBaseUnits(-exponent);
		if (exponent > 28)
			throw new OverflowException(name + " exponent is too large.");
		return checked((decimal)integer * Pow10(exponent));
	}

	public static decimal PriceStep(int exponent)
		=> exponent <= 0 ? 1m / Pow10(-exponent) : Pow10(exponent);

	public static DateTime FromUnixMicrosecondsUtc(this string microseconds)
	{
		var value = microseconds.ThrowIfEmpty(nameof(microseconds))
			.ParseInteger();
		if (value < 0 || value > long.MaxValue)
			throw new InvalidDataException(
				"Invalid microsecond timestamp '" + value + "'.");
		return ((long)value).FromUnixMcs();
	}

	public static DateTime EnsureAvantisUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc
			? value
			: value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

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

	public static string OrderKey(int pairIndex, int tradeIndex)
	{
		if (pairIndex < 0 || tradeIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return pairIndex.ToString(CultureInfo.InvariantCulture) + ":" +
			tradeIndex.ToString(CultureInfo.InvariantCulture);
	}

	public static (int PairIndex, int TradeIndex) ParseOrderKey(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var separator = value.IndexOf(':');
		if (separator <= 0 || separator == value.Length - 1 ||
			!int.TryParse(value[..separator], NumberStyles.None,
				CultureInfo.InvariantCulture, out var pairIndex) ||
			!int.TryParse(value[(separator + 1)..], NumberStyles.None,
				CultureInfo.InvariantCulture, out var tradeIndex) ||
			pairIndex < 0 || tradeIndex < 0)
			throw new ArgumentException(
				"Avantis order identifier must use the pair:index format.",
				nameof(value));
		return (pairIndex, tradeIndex);
	}

	private static decimal Pow10(int exponent)
	{
		if (exponent is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(exponent));
		var result = 1m;
		for (var index = 0; index < exponent; index++)
			result *= 10m;
		return result;
	}
}
