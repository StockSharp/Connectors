namespace StockSharp.Ostium.Native;

static class OstiumExtensions
{
	public const string BuilderEndpoint = "https://builder.ostium.io";
	public const string ZeroAddress =
		"0x0000000000000000000000000000000000000000";
	public const decimal MinimumCollateral = 5m;

	public static readonly string OpenLimitPlacedTopic = AbiTopic(
		"OpenLimitPlaced(address,uint16,uint8)");
	public static readonly string OpenLimitPlacedV2Topic = AbiTopic(
		"OpenLimitPlacedV2(address,uint16,uint8,(uint256,uint192,uint192,uint192,address,uint32,uint16,uint8,bool,bool),uint8,(address,uint32))");
	public static readonly string MarketOpenOrderInitiatedTopic = AbiTopic(
		"MarketOpenOrderInitiated(uint256,address,uint16)");
	public static readonly string MarketCloseOrderInitiatedV2Topic = AbiTopic(
		"MarketCloseOrderInitiatedV2(uint256,uint256,address,uint16,uint16)");

	public static OstiumNetwork GetNetwork(this OstiumEnvironments environment)
		=> environment switch
		{
			OstiumEnvironments.Mainnet => new()
			{
				ChainId = 42161,
				Name = "Arbitrum One",
				RpcEndpoint = "https://arb1.arbitrum.io/rpc",
				SubgraphEndpoint =
					"https://builder.ostium.io/v1/subgraph/gn",
				UsdcAddress =
					"0xaf88d065e77c8cC2239327C5EDb3A432268e5831",
				TradingAddress =
					"0x6D0bA1f9996DBD8885827e1b2e8f6593e7702411",
				TradingStorageAddress =
					"0xcCd5891083A8acD2074690F65d3024E7D13d66E7",
			},
			OstiumEnvironments.Testnet => new()
			{
				ChainId = 421614,
				Name = "Arbitrum Sepolia",
				RpcEndpoint = "https://sepolia-rollup.arbitrum.io/rpc",
				SubgraphEndpoint =
					"https://api.subgraph.ormilabs.com/api/public/67a599d5-c8d2-4cc4-9c4d-2975a97bc5d8/subgraphs/ost-sep/live/gn",
				UsdcAddress =
					"0xe73B11Fb1e3eeEe8AF2a23079A4410Fe1B370548",
				TradingAddress =
					"0x2A9B9c988393f46a2537B0ff11E98c2C15a95afe",
				TradingStorageAddress =
					"0x0b9F5243B29938668c9Cfbd7557A389EC7Ef88b8",
			},
			_ => throw new ArgumentOutOfRangeException(nameof(environment)),
		};

	public static SecurityId ToStockSharp(this OstiumMarket market)
		=> new()
		{
			SecurityCode = market.Symbol,
			BoardCode = BoardCodes.Ostium,
		};

	public static string NormalizePairName(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"CL" => "WTI",
			"HG" => "XCU",
			"SPX" => "US500",
			"NDX" => "US100",
			"DJI" => "US30",
			"DAX" => "GER40",
			"FTSE" => "UK100",
			"HSI" => "HK50",
			"NIK" => "JP225",
			var normalized => normalized,
		};

	public static CurrencyTypes? ToOstiumCurrency(this string value)
		=> value?.ToUpperInvariant() switch
		{
			"USD" or "USDC" => CurrencyTypes.USD,
			"EUR" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			"CHF" => CurrencyTypes.CHF,
			"CAD" => CurrencyTypes.CAD,
			"AUD" => CurrencyTypes.AUD,
			"NZD" => CurrencyTypes.NZD,
			"HKD" => CurrencyTypes.HKD,
			"CNY" or "CNH" => CurrencyTypes.CNY,
			_ => null,
		};

	public static string ToOstiumResolution(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15"
			: timeFrame == TimeSpan.FromHours(1) ? "60"
			: timeFrame == TimeSpan.FromHours(4) ? "240"
			: timeFrame == TimeSpan.FromDays(1) ? "1D"
			: throw new NotSupportedException(
				"Ostium does not support the " + timeFrame +
				" candle time-frame.");

	public static decimal ParseScaled(this string value, int decimals,
		string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (value.Contains('.', StringComparison.Ordinal))
		{
			if (!decimal.TryParse(value, NumberStyles.Number,
				CultureInfo.InvariantCulture, out var decimalValue))
				throw new InvalidDataException(
					"Ostium returned an invalid " + name + " '" + value + "'.");
			return decimalValue;
		}
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var raw))
			throw new InvalidDataException(
				"Ostium returned an invalid " + name + " '" + value + "'.");
		return raw.FromBaseUnits(decimals);
	}

	public static decimal? TryParseScaled(this string value, int decimals)
	{
		if (value.IsEmpty())
			return null;
		try
		{
			return value.ParseScaled(decimals, "numeric value");
		}
		catch (Exception)
		{
			return null;
		}
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals,
		string name)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(name, value,
				"Ostium numeric values cannot be negative.");
		var scale = BigInteger.Pow(10, decimals);
		var scaled = value * (decimal)scale;
		if (scaled != decimal.Truncate(scaled))
			throw new ArgumentOutOfRangeException(name, value,
				"Ostium value has more than " + decimals +
				" decimal places.");
		return new BigInteger(scaled);
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		var scale = BigInteger.Pow(10, decimals);
		var quotient = BigInteger.DivRem(value, scale, out var remainder);
		if (quotient > new BigInteger(decimal.MaxValue) ||
			quotient < new BigInteger(decimal.MinValue))
			throw new OverflowException("Ostium value exceeds decimal range.");
		return (decimal)quotient + (decimal)remainder / (decimal)scale;
	}

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

	public static BigInteger ParseInteger(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var isHex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
		if (isHex)
			value = value[2..];
		if (value.IsEmpty())
			return BigInteger.Zero;
		if (!BigInteger.TryParse(isHex ? "0" + value : value,
			isHex ? NumberStyles.AllowHexSpecifier : NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException("Invalid integer value.");
		return result;
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	public static DateTime EnsureOstiumUtc(this DateTime value)
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

	public static string OrderKey(int pairIndex, int positionIndex)
	{
		if (pairIndex < 0 || positionIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return pairIndex.ToString(CultureInfo.InvariantCulture) + ":" +
			positionIndex.ToString(CultureInfo.InvariantCulture);
	}

	public static (int PairIndex, int PositionIndex) ParseOrderKey(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var separator = value.IndexOf(':');
		if (separator <= 0 || separator == value.Length - 1 ||
			!int.TryParse(value[..separator], NumberStyles.None,
				CultureInfo.InvariantCulture, out var pairIndex) ||
			!int.TryParse(value[(separator + 1)..], NumberStyles.None,
				CultureInfo.InvariantCulture, out var positionIndex) ||
			pairIndex < 0 || positionIndex < 0)
			throw new ArgumentException(
				"Ostium order identifier must use the pair:index format.",
				nameof(value));
		return (pairIndex, positionIndex);
	}
}
