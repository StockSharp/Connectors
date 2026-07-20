namespace StockSharp.Gmx.Native;

static class GmxExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames = new()
	{
		[TimeSpan.FromMinutes(1)] = "1m",
		[TimeSpan.FromMinutes(5)] = "5m",
		[TimeSpan.FromMinutes(15)] = "15m",
		[TimeSpan.FromHours(1)] = "1h",
		[TimeSpan.FromHours(4)] = "4h",
		[TimeSpan.FromDays(1)] = "1d",
	};

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToGmxTimeFrame(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new NotSupportedException(
				"GMX does not support the " + timeFrame + " candle interval.");

	public static DateTime EnsureGmxUtc(this DateTime time)
		=> time.Kind switch
		{
			DateTimeKind.Utc => time,
			DateTimeKind.Local => time.ToUniversalTime(),
			_ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
		};

	public static DateTime FromGmxMilliseconds(this long timestamp)
	{
		if (timestamp <= 0)
			throw new InvalidDataException(
				"GMX returned a non-positive millisecond timestamp.");
		try
		{
			return DateTime.UnixEpoch.AddMilliseconds(timestamp);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"GMX returned an out-of-range millisecond timestamp.", error);
		}
	}

	public static DateTime FromGmxSeconds(this long timestamp)
	{
		if (timestamp <= 0)
			throw new InvalidDataException(
				"GMX returned a non-positive second timestamp.");
		try
		{
			return DateTime.UnixEpoch.AddSeconds(timestamp);
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				"GMX returned an out-of-range second timestamp.", error);
		}
	}

	public static long ToGmxMilliseconds(this DateTime time)
		=> checked((long)(time.EnsureGmxUtc() - DateTime.UnixEpoch)
			.TotalMilliseconds);

	public static long ToGmxSeconds(this DateTime time)
		=> checked((long)(time.EnsureGmxUtc() - DateTime.UnixEpoch)
			.TotalSeconds);

	public static BigInteger ParseGmxInteger(this string value, string field,
		bool isNegativeAllowed = false)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result) ||
			(!isNegativeAllowed && result < 0))
			throw new InvalidDataException("GMX returned an invalid " + field + ".");
		return result;
	}

	public static decimal ParseGmxScaled(this string value, int decimals,
		string field, bool isNegativeAllowed = false)
		=> FromScaledInteger(value.ParseGmxInteger(field, isNegativeAllowed),
			decimals, field);

	public static decimal? TryParseGmxScaled(this string value, int decimals,
		bool isNegativeAllowed = false)
	{
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var integer) ||
			(!isNegativeAllowed && integer < 0))
			return null;
		try
		{
			return FromScaledInteger(integer, decimals, "numeric value");
		}
		catch (InvalidDataException)
		{
			return null;
		}
	}

	public static decimal FromScaledInteger(BigInteger value, int decimals,
		string field)
	{
		if (decimals < 0)
			throw new ArgumentOutOfRangeException(nameof(decimals), decimals, null);
		var isNegative = value.Sign < 0;
		var digits = BigInteger.Abs(value).ToString(CultureInfo.InvariantCulture);
		string text;
		if (decimals == 0)
			text = digits;
		else if (digits.Length <= decimals)
			text = "0." + new string('0', decimals - digits.Length) + digits;
		else
			text = digits.Insert(digits.Length - decimals, ".");
		if (isNegative)
			text = "-" + text;
		if (!decimal.TryParse(text, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				"GMX " + field + " does not fit in a decimal value.");
		return result;
	}

	public static BigInteger ToScaledInteger(this decimal value, int decimals,
		string field)
	{
		if (decimals < 0)
			throw new ArgumentOutOfRangeException(nameof(decimals), decimals, null);
		var text = value.ToString("0.############################",
			CultureInfo.InvariantCulture);
		var isNegative = text.StartsWith("-", StringComparison.Ordinal);
		if (isNegative)
			text = text[1..];
		var point = text.IndexOf('.');
		var fraction = point < 0 ? 0 : text.Length - point - 1;
		var digits = point < 0 ? text : text.Remove(point, 1);
		if (fraction > decimals)
			throw new InvalidOperationException(
				"GMX " + field + " has more than " + decimals +
				" supported decimals.");
		if (!BigInteger.TryParse(digits, NumberStyles.None,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidOperationException("GMX " + field + " is invalid.");
		result *= BigInteger.Pow(10, decimals - fraction);
		return isNegative ? -result : result;
	}

	public static string ToGmxScaled(this decimal value, int decimals,
		string field)
		=> value.ToScaledInteger(decimals, field)
			.ToString(CultureInfo.InvariantCulture);

	public static decimal ParseGmxUsd(this string value, string field,
		bool isNegativeAllowed = false)
		=> value.ParseGmxScaled(30, field, isNegativeAllowed);

	public static decimal? TryParseGmxUsd(this string value,
		bool isNegativeAllowed = false)
		=> value.TryParseGmxScaled(30, isNegativeAllowed);

	public static decimal ParseGmxContractPrice(this string value,
		int tokenDecimals, string field)
	{
		if (tokenDecimals is < 0 or > 36)
			throw new InvalidDataException("GMX token decimals are invalid.");
		var scaled = value.ParseGmxInteger(field) *
			BigInteger.Pow(10, tokenDecimals);
		return FromScaledInteger(scaled, 30, field);
	}

	public static decimal VolumeStep(int decimals)
	{
		decimals = decimals.Max(0).Min(8);
		return decimals == 0 ? 1m : 1m / (decimal)Math.Pow(10, decimals);
	}

	public static SecurityId ToStockSharp(this GmxMarket market)
		=> new()
		{
			SecurityCode = market.Symbol,
			BoardCode = BoardCodes.Gmx,
		};

	public static SecurityTypes ToSecurityType(this GmxMarket market)
		=> market.IsSpotOnly
			? SecurityTypes.CryptoCurrency
			: SecurityTypes.Future;

	public static OrderTypes ToStockSharp(this GmxApiOrderTypes orderType)
		=> orderType switch
		{
			GmxApiOrderTypes.MarketSwap or
			GmxApiOrderTypes.MarketIncrease or
			GmxApiOrderTypes.MarketDecrease or
			GmxApiOrderTypes.Liquidation => OrderTypes.Market,
			GmxApiOrderTypes.LimitSwap or
			GmxApiOrderTypes.LimitIncrease => OrderTypes.Limit,
			GmxApiOrderTypes.LimitDecrease or
			GmxApiOrderTypes.StopLossDecrease or
			GmxApiOrderTypes.StopIncrease => OrderTypes.Conditional,
			_ => OrderTypes.Conditional,
		};

	public static Sides ToStockSharpSide(this GmxApiOrderTypes orderType,
		bool isLong)
		=> orderType switch
		{
			GmxApiOrderTypes.MarketDecrease or
			GmxApiOrderTypes.LimitDecrease or
			GmxApiOrderTypes.StopLossDecrease or
			GmxApiOrderTypes.Liquidation => isLong ? Sides.Sell : Sides.Buy,
			_ => isLong ? Sides.Buy : Sides.Sell,
		};

	public static bool IsDecrease(this GmxApiOrderTypes orderType)
		=> orderType is GmxApiOrderTypes.MarketDecrease or
			GmxApiOrderTypes.LimitDecrease or
			GmxApiOrderTypes.StopLossDecrease or
			GmxApiOrderTypes.Liquidation;

	public static string NormalizeGmxAddress(this string value, string field,
		bool isEmptyAllowed = false)
	{
		if (value.IsEmpty())
		{
			if (isEmptyAllowed)
				return null;
			throw new ArgumentNullException(field);
		}
		value = value.Trim();
		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			value.Length != 42 || value.Skip(2).Any(static ch => !Uri.IsHexDigit(ch)))
			throw new ArgumentException(
				"GMX " + field + " must be a 20-byte EVM address.", field);
		return "0x" + value[2..].ToLowerInvariant();
	}

	public static long ChainId(this GmxNetworks network)
		=> network switch
		{
			GmxNetworks.Arbitrum => 42161,
			GmxNetworks.Avalanche => 43114,
			GmxNetworks.MegaEth => 4326,
			_ => throw new ArgumentOutOfRangeException(nameof(network), network,
				null),
		};

	public static string NetworkName(this GmxNetworks network)
		=> network switch
		{
			GmxNetworks.Arbitrum => "Arbitrum",
			GmxNetworks.Avalanche => "Avalanche",
			GmxNetworks.MegaEth => "MegaETH",
			_ => throw new ArgumentOutOfRangeException(nameof(network), network,
				null),
		};

	public static string ApiEndpoint(this GmxNetworks network, bool isSecondary)
	{
		var peer = isSecondary ? "gmxapi.ai" : "gmxapi.io";
		return network switch
		{
			GmxNetworks.Arbitrum => "https://arbitrum." + peer,
			GmxNetworks.Avalanche => "https://avalanche." + peer,
			GmxNetworks.MegaEth => "https://megaeth." + peer,
			_ => throw new ArgumentOutOfRangeException(nameof(network), network,
				null),
		};
	}

	public static string RelayRouter(this GmxNetworks network)
		=> network switch
		{
			GmxNetworks.Arbitrum =>
				"0xa9090e2fd6cd8ee397cf3106189a7e1cfae6c59c",
			GmxNetworks.Avalanche =>
				"0xee2d3339cbce7a42573c96acc1298a79a5c996df",
			GmxNetworks.MegaEth =>
				"0x24ed625b9c47fdebf088a4d12b7f9b4b2f556297",
			_ => throw new ArgumentOutOfRangeException(nameof(network), network,
				null),
		};

	public static string DefaultCollateral(this GmxNetworks network)
		=> network switch
		{
			GmxNetworks.Arbitrum or GmxNetworks.Avalanche => "USDC",
			GmxNetworks.MegaEth => "USDC",
			_ => throw new ArgumentOutOfRangeException(nameof(network), network,
				null),
		};

	public static CurrencyTypes? ToCurrency(this string value)
		=> Enum.TryParse<CurrencyTypes>(value, true, out var result)
			? result
			: null;
}
