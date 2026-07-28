namespace StockSharp.Chainflip.Native;

static class ChainflipExtensions
{
	public const string ProbeAddress =
		"0x000000000000000000000000000000000000dEaD";

	private static readonly Dictionary<string, ChainflipAsset> _assets =
		CreateAssets().ToDictionary(static asset => asset.Key,
			StringComparer.OrdinalIgnoreCase);

	public static ChainflipAsset[] Assets => [.. _assets.Values];

	public static ChainflipAsset ResolveAsset(this ChainflipRpcAsset value)
	{
		ArgumentNullException.ThrowIfNull(value);
		var key = $"{value.Chain?.Trim()}:{value.Symbol?.Trim()}";
		return _assets.TryGetValue(key, out var asset)
			? asset
			: throw new InvalidDataException(
				$"Chainflip returned unsupported asset '{key}'.");
	}

	public static ChainflipRpcAsset ToRpc(this ChainflipAsset asset)
	{
		ArgumentNullException.ThrowIfNull(asset);
		return new()
		{
			Chain = asset.Chain,
			Symbol = asset.Symbol,
		};
	}

	public static string ToSecurityCode(this ChainflipAsset baseAsset,
		ChainflipAsset quoteAsset)
	{
		ArgumentNullException.ThrowIfNull(baseAsset);
		ArgumentNullException.ThrowIfNull(quoteAsset);
		return ($"{baseAsset.Symbol}@{baseAsset.Chain}-" +
			$"{quoteAsset.Symbol}@{quoteAsset.Chain}").ToUpperInvariant();
	}

	public static SecurityId ToStockSharp(this ChainflipMarket market)
	{
		ArgumentNullException.ThrowIfNull(market);
		return new()
		{
			SecurityCode = market.SecurityCode,
			BoardCode = BoardCodes.Chainflip,
		};
	}

	public static decimal DecodeSqrtPrice(string value,
		int baseDecimals, int quoteDecimals)
	{
		var sqrt = value.ParseInteger();
		if (sqrt <= 0)
			throw new InvalidDataException(
				"Chainflip square-root price must be positive.");
		var ratio = (double)sqrt / Math.Pow(2d, 96d);
		var price = ratio * ratio *
			Math.Pow(10d, baseDecimals - quoteDecimals);
		if (!double.IsFinite(price) || price <= 0 ||
			price > (double)decimal.MaxValue)
			throw new InvalidDataException(
				"Chainflip square-root price is outside the decimal range.");
		return (decimal)price;
	}

	public static BigInteger GetMinimumPriceX128(string estimatedPrice,
		ChainflipAsset source, ChainflipAsset destination,
		decimal slippageTolerance)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (slippageTolerance is < 0 or > 100)
			throw new ArgumentOutOfRangeException(
				nameof(slippageTolerance));
		var price = decimal.Parse(
			estimatedPrice.ThrowIfEmpty(nameof(estimatedPrice)),
			NumberStyles.Number, CultureInfo.InvariantCulture);
		var minimum = price * (100m - slippageTolerance) / 100m;
		if (minimum <= 0)
			throw new InvalidDataException(
				"Chainflip minimum execution price must be positive.");
		var bits = decimal.GetBits(minimum);
		var unscaled = new BigInteger((uint)bits[0]) |
			(new BigInteger((uint)bits[1]) << 32) |
			(new BigInteger((uint)bits[2]) << 64);
		var scale = (bits[3] >> 16) & 0x7f;
		var numerator = unscaled * BigInteger.Pow(2, 128) *
			BigInteger.Pow(10, destination.Decimals);
		var denominator = BigInteger.Pow(10, scale + source.Decimals);
		var result = numerator / denominator;
		return result > 0
			? result
			: throw new InvalidDataException(
				"Chainflip minimum execution price rounds to zero.");
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
		=> value.ToBaseUnitsCore(decimals, false);

	public static BigInteger ToBaseUnitsCeiling(this decimal value,
		int decimals)
		=> value.ToBaseUnitsCore(decimals, true);

	private static BigInteger ToBaseUnitsCore(this decimal value,
		int decimals, bool ceiling)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var bits = decimal.GetBits(value);
		var unscaled = new BigInteger((uint)bits[0]) |
			(new BigInteger((uint)bits[1]) << 32) |
			(new BigInteger((uint)bits[2]) << 64);
		var scale = (bits[3] >> 16) & 0x7f;
		if (scale <= decimals)
			return unscaled * BigInteger.Pow(10, decimals - scale);
		var divisor = BigInteger.Pow(10, scale - decimals);
		var quotient = BigInteger.DivRem(unscaled, divisor,
			out var remainder);
		return ceiling && remainder > 0 ? quotient + 1 : quotient;
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var negative = value.Sign < 0;
		var digits = BigInteger.Abs(value).ToString(
			CultureInfo.InvariantCulture);
		string text;
		if (decimals == 0)
			text = digits;
		else if (digits.Length <= decimals)
			text = "0." + new string('0', decimals - digits.Length) +
				digits;
		else
			text = digits.Insert(digits.Length - decimals, ".");
		if (negative)
			text = "-" + text;
		return decimal.Parse(text, NumberStyles.Number,
			CultureInfo.InvariantCulture);
	}

	public static decimal GetUnitStep(this int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		return 1m / (decimal)BigInteger.Pow(10, decimals);
	}

	public static BigInteger ParseInteger(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			var hex = value[2..];
			if (hex.Length == 0 || hex.Any(static ch => !Uri.IsHexDigit(ch)))
				throw new FormatException($"Invalid hexadecimal value '{value}'.");
			return BigInteger.Parse("0" + hex,
				NumberStyles.AllowHexSpecifier,
				CultureInfo.InvariantCulture);
		}
		return BigInteger.Parse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture);
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	public static string NormalizeAddress(this string address)
	{
		address = address.ThrowIfEmpty(nameof(address)).Trim();
		if (!AddressUtil.Current.IsValidEthereumAddressHexFormat(address))
			throw new ArgumentException(
				$"Invalid EVM address '{address}'.", nameof(address));
		return AddressUtil.Current.ConvertToChecksumAddress(address);
	}

	public static string NormalizeHash(this string hash)
	{
		hash = hash.ThrowIfEmpty(nameof(hash)).Trim();
		if (!hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			hash.Length != 66 ||
			hash[2..].Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				$"Invalid EVM transaction hash '{hash}'.", nameof(hash));
		return "0x" + hash[2..].ToLowerInvariant();
	}

	public static string NormalizeData(this string data)
	{
		data = data.ThrowIfEmpty(nameof(data)).Trim();
		if (!data.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			data.Length <= 2 || (data.Length - 2) % 2 != 0 ||
			data[2..].Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				"Invalid EVM transaction calldata.", nameof(data));
		return "0x" + data[2..].ToLowerInvariant();
	}

	public static string AbiAddress(string address)
		=> address.NormalizeAddress()[2..].PadLeft(64, '0').ToLowerInvariant();

	public static string AbiWord(BigInteger value)
	{
		if (value < 0 || value >= BigInteger.Pow(2, 256))
			throw new ArgumentOutOfRangeException(nameof(value));
		return value.ToString("x", CultureInfo.InvariantCulture)
			.PadLeft(64, '0');
	}

	public static string EncodeStaticCall(string signature,
		params string[] words)
	{
		var selector = new Sha3Keccack().CalculateHash(
			signature.ThrowIfEmpty(nameof(signature)))[..8];
		return "0x" + selector + string.Concat(words ?? []);
	}

	public static CurrencyTypes? ToCurrency(this string value)
	{
		value = value?.Trim();
		if (value.IsEmpty())
			return null;
		return value.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" =>
				CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"BTC" or "WBTC" => CurrencyTypes.BTC,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency)
					? currency
					: null,
		};
	}

	public static DateTime ToUtcTime(this long unixTime)
		=> unixTime > 10_000_000_000
			? DateTimeOffset.FromUnixTimeMilliseconds(unixTime).UtcDateTime
			: DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;

	public static int GetEvmChainId(this string chain)
		=> chain?.Trim().ToUpperInvariant() switch
		{
			"ETHEREUM" => 1,
			"ARBITRUM" => 42161,
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Chainflip vault signing supports Ethereum and Arbitrum."),
		};

	public static string GetDefaultRpcEndpoint(this string chain)
		=> chain?.Trim().ToUpperInvariant() switch
		{
			"ETHEREUM" => "https://ethereum-rpc.publicnode.com",
			"ARBITRUM" => "https://arbitrum-one-rpc.publicnode.com",
			_ => throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Chainflip vault signing supports Ethereum and Arbitrum."),
		};

	private static IEnumerable<ChainflipAsset> CreateAssets()
	{
		yield return Asset("Ethereum", "ETH", 18);
		yield return Asset("Ethereum", "FLIP", 18,
			"0x826180541412D574cf1336d22c0C0a287822678A");
		yield return Asset("Ethereum", "USDC", 6,
			"0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48");
		yield return Asset("Ethereum", "USDT", 6,
			"0xdAC17F958D2ee523a2206206994597C13D831ec7");
		yield return Asset("Ethereum", "WBTC", 8,
			"0x2260FAC5E5542a773Aa44fBCfeDf7C193bc2C599");
		yield return Asset("Polkadot", "DOT", 10);
		yield return Asset("Bitcoin", "BTC", 8);
		yield return Asset("Arbitrum", "ETH", 18);
		yield return Asset("Arbitrum", "USDC", 6,
			"0xaf88d065e77c8cC2239327C5EDb3A432268e5831");
		yield return Asset("Arbitrum", "USDT", 6,
			"0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9");
		yield return Asset("Solana", "SOL", 9);
		yield return Asset("Solana", "USDC", 6);
		yield return Asset("Solana", "USDT", 6);
		yield return Asset("Assethub", "DOT", 10);
		yield return Asset("Assethub", "USDT", 6);
		yield return Asset("Assethub", "USDC", 6);
		yield return Asset("Tron", "TRX", 6);
		yield return Asset("Tron", "USDT", 6);
	}

	private static ChainflipAsset Asset(string chain, string symbol,
		int decimals, string contractAddress = null)
		=> new()
		{
			Chain = chain,
			Symbol = symbol,
			Decimals = decimals,
			ContractAddress = contractAddress?.NormalizeAddress(),
		};
}
