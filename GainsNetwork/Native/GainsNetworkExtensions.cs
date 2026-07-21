namespace StockSharp.GainsNetwork.Native;

static class GainsNetworkExtensions
{
	public const string ZeroAddress =
		"0x0000000000000000000000000000000000000000";
	public const decimal PricePrecision = 10_000_000_000m;
	public const decimal LeveragePrecision = 1_000m;

	private static readonly GainsNetworkDeployment[] _deployments =
	[
		new()
		{
			Environment = GainsNetworkEnvironments.Arbitrum,
			Name = "Arbitrum One",
			ChainId = 42161,
			RpcEndpoint = "https://arb1.arbitrum.io/rpc",
			BackendEndpoint = "https://backend-arbitrum.gains.trade",
			DiamondAddress =
				"0xFF162c694eAA571f685030649814282eA457f169",
			NativeSymbol = "ETH",
		},
		new()
		{
			Environment = GainsNetworkEnvironments.Base,
			Name = "Base",
			ChainId = 8453,
			RpcEndpoint = "https://mainnet.base.org",
			BackendEndpoint = "https://backend-base.gains.trade",
			DiamondAddress =
				"0x6cD5aC19a07518A8092eEFfDA4f1174C72704eeb",
			NativeSymbol = "ETH",
		},
		new()
		{
			Environment = GainsNetworkEnvironments.Polygon,
			Name = "Polygon PoS",
			ChainId = 137,
			RpcEndpoint = "https://polygon.drpc.org",
			BackendEndpoint = "https://backend-polygon.gains.trade",
			DiamondAddress =
				"0x209A9A01980377916851af2cA075C2b170452018",
			NativeSymbol = "POL",
		},
	];

	public static GainsNetworkDeployment GetDeployment(
		this GainsNetworkEnvironments environment)
		=> _deployments.FirstOrDefault(item => item.Environment == environment) ??
			throw new ArgumentOutOfRangeException(nameof(environment), environment,
				"Unsupported Gains Network deployment.");

	public static string NormalizeAddress(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 40 || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException("Invalid EVM address.", nameof(value));
		return "0x" + value.ToLowerInvariant();
	}

	public static string NormalizeHash(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			value = value[2..];
		if (value.Length != 64 || value.Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException("Invalid EVM transaction hash.",
				nameof(value));
		return "0x" + value.ToLowerInvariant();
	}

	public static BigInteger ParseInteger(this string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		if (isHex)
			value = value[2..];
		if (value.IsEmpty())
			return BigInteger.Zero;
		if (!BigInteger.TryParse(isHex ? "0" + value : value,
			isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException("Invalid Gains " + name + ".");
		return result;
	}

	public static decimal ParseScaled(this string value, int decimals,
		string name)
	{
		var raw = value.ParseInteger(name);
		if (decimals is < 0 or > 28)
			throw new InvalidDataException("Unsupported Gains " + name +
				" decimal precision.");
		return (decimal)raw / Pow10(decimals);
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals,
		string name)
	{
		if (value < 0 || decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(name, value,
				"Gains value is outside the supported range.");
		var scaled = decimal.Round(value * Pow10(decimals), 0,
			MidpointRounding.AwayFromZero);
		return new BigInteger(scaled);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 28 || value < (BigInteger)decimal.MinValue ||
			value > (BigInteger)decimal.MaxValue)
			throw new InvalidDataException(
				"Gains integer is outside the decimal range.");
		return (decimal)value / Pow10(decimals);
	}

	private static decimal Pow10(int decimals)
	{
		decimal result = 1m;
		for (var i = 0; i < decimals; i++)
			result *= 10m;
		return result;
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc
			? value
			: value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

	public static DateTime ParseTime(this string value, string name)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var result))
			throw new InvalidDataException("Invalid Gains " + name + ".");
		return result.EnsureUtc();
	}

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

	public static int ParseIndex(this string value, string name)
	{
		if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture,
			out var result) || result < 0)
			throw new InvalidDataException("Invalid Gains " + name + ".");
		return result;
	}

	public static SecurityId ToStockSharp(this GainsMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.Symbol,
			BoardCode = BoardCodes.GainsNetwork,
		};
	}
}
