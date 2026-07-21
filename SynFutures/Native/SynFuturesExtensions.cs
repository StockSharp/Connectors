namespace StockSharp.SynFutures.Native;

static class SynFuturesExtensions
{
	public const int ChainId = 8453;
	public const uint PerpetualExpiry = uint.MaxValue;
	public const int OrderSpacing = 5;
	public const int WadDecimals = 18;
	public const string UsdcAddress =
		"0x833589fcd6edb6e08f4c7c32d4f71b54bda02913";

	private static readonly BigInteger _two128 = BigInteger.One << 128;
	private static readonly BigInteger _two24 = BigInteger.One << 24;
	private static readonly BigInteger _minimumInt128 =
		-(BigInteger.One << 127);
	private static readonly BigInteger _maximumInt128 =
		(BigInteger.One << 127) - 1;

	public static readonly string PlaceTopic = AbiTopic(
		"Place(uint32,address,int24,uint32,(uint128,int128))");
	public static readonly string TradeTopic = AbiTopic(
		"Trade(uint32,address,int256,uint256,int256,uint256,uint256,uint16,uint160,uint256)");

	public static SecurityId ToStockSharp(this SynFuturesMarket market)
		=> new()
		{
			SecurityCode = market?.Symbol,
			BoardCode = BoardCodes.SynFutures,
		};

	public static string NormalizeAddress(this string address)
	{
		address = address.ThrowIfEmpty(nameof(address)).Trim();
		if (!address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			address = "0x" + address;
		if (address.Length != 42 || address[2..].Any(
			static value => !Uri.IsHexDigit(value)))
			throw new FormatException(
				"Invalid EVM address '" + address + "'.");
		return address.ToLowerInvariant();
	}

	public static string NormalizeHash(this string hash)
	{
		hash = hash.ThrowIfEmpty(nameof(hash)).Trim();
		if (!hash.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			hash = "0x" + hash;
		if (hash.Length != 66 || hash[2..].Any(
			static value => !Uri.IsHexDigit(value)))
			throw new FormatException("Invalid EVM hash '" + hash + "'.");
		return hash.ToLowerInvariant();
	}

	public static BigInteger ParseInteger(this string value,
		string name = null)
	{
		value = value.ThrowIfEmpty(name ?? nameof(value)).Trim();
		if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			if (!BigInteger.TryParse("0" + value[2..],
				NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
				out var hex))
				throw new InvalidDataException(
					"SynFutures returned an invalid " + (name ?? "integer") +
					" '" + value + "'.");
			return hex;
		}
		if (!BigInteger.TryParse(value, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				"SynFutures returned an invalid " + (name ?? "integer") +
				" '" + value + "'.");
		return result;
	}

	public static BigInteger ParseIntegerOrZero(this string value,
		string name = null)
		=> value.IsEmpty() ? BigInteger.Zero : value.ParseInteger(name);

	public static decimal ParseDecimal(this string value, string name)
	{
		if (!decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				"SynFutures returned an invalid " + name + " '" + value + "'.");
		return result;
	}

	public static decimal? TryParseDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var result)
			? result
			: null;

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scale = BigInteger.Pow(10, decimals);
		return checked((decimal)(value / scale)) +
			checked((decimal)(value % scale)) / checked((decimal)scale);
	}

	public static BigInteger ToBaseUnits(this decimal value, int decimals,
		string name)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var scale = decimal.One;
		for (var i = 0; i < decimals; i++)
			scale *= 10m;
		var scaled = decimal.Truncate(value * scale);
		if (scaled != value * scale)
			throw new ArgumentOutOfRangeException(name, value,
				"SynFutures value has more than " + decimals +
				" decimal places.");
		return new BigInteger(scaled);
	}

	public static DateTime ToUtc(this long seconds)
	{
		if (seconds <= 0)
			throw new InvalidDataException(
				"SynFutures returned an invalid Unix timestamp '" + seconds + "'.");
		return seconds.FromUnix().EnsureUtc();
	}

	public static DateTime EnsureUtc(this DateTime value)
		=> value.Kind == DateTimeKind.Utc
			? value
			: value.Kind == DateTimeKind.Local
				? value.ToUniversalTime()
				: DateTime.SpecifyKind(value, DateTimeKind.Utc);

	public static string ToApiInterval(this TimeSpan timeFrame)
		=> timeFrame == TimeSpan.FromMinutes(1) ? "1m"
			: timeFrame == TimeSpan.FromMinutes(5) ? "5m"
			: timeFrame == TimeSpan.FromMinutes(15) ? "15m"
			: timeFrame == TimeSpan.FromMinutes(30) ? "30m"
			: timeFrame == TimeSpan.FromHours(1) ? "1h"
			: timeFrame == TimeSpan.FromHours(4) ? "4h"
			: timeFrame == TimeSpan.FromDays(1) ? "1d"
			: timeFrame == TimeSpan.FromDays(7) ? "1w"
			: throw new NotSupportedException(
				"SynFutures does not support " + timeFrame + " candles.");

	public static CurrencyTypes? ToCurrency(this string symbol)
		=> symbol?.ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" => CurrencyTypes.USD,
			"EUR" => CurrencyTypes.EUR,
			"GBP" => CurrencyTypes.GBP,
			"JPY" => CurrencyTypes.JPY,
			_ => null,
		};

	public static int PriceToTick(decimal price)
	{
		if (price <= 0)
			throw new ArgumentOutOfRangeException(nameof(price));
		var tick = (long)Math.Floor(Math.Log((double)price) /
			Math.Log(1.0001d));
		if (tick is < -8_388_608 or > 8_388_607)
			throw new ArgumentOutOfRangeException(nameof(price), price,
				"SynFutures price is outside the int24 tick range.");
		return (int)tick;
	}

	public static decimal TickToPrice(int tick)
	{
		if (tick is < -8_388_608 or > 8_388_607)
			throw new ArgumentOutOfRangeException(nameof(tick));
		var value = Math.Pow(1.0001d, tick);
		if (double.IsNaN(value) || double.IsInfinity(value) ||
			value <= 0 || value > (double)decimal.MaxValue)
			throw new InvalidDataException(
				"SynFutures tick cannot be represented as a decimal price.");
		return (decimal)value;
	}

	public static int AlignOrderTick(int tick)
		=> checked(OrderSpacing * (int)Math.Round(
			tick / (double)OrderSpacing, MidpointRounding.AwayFromZero));

	public static string CreateOrderKey(string instrument, uint expiry,
		int tick, uint nonce)
		=> instrument.NormalizeAddress() + ":" + expiry.ToString(
			CultureInfo.InvariantCulture) + ":" + tick.ToString(
			CultureInfo.InvariantCulture) + ":" + nonce.ToString(
			CultureInfo.InvariantCulture);

	public static (string Instrument, uint Expiry, int Tick, uint Nonce)
		ParseOrderKey(string value)
	{
		var parts = value.ThrowIfEmpty(nameof(value)).Split(':');
		if (parts.Length != 4 ||
			!uint.TryParse(parts[1], NumberStyles.None,
				CultureInfo.InvariantCulture, out var expiry) ||
			!int.TryParse(parts[2], NumberStyles.AllowLeadingSign,
				CultureInfo.InvariantCulture, out var tick) ||
			!uint.TryParse(parts[3], NumberStyles.None,
				CultureInfo.InvariantCulture, out var nonce))
			throw new FormatException(
				"Invalid SynFutures order identifier '" + value + "'.");
		return (parts[0].NormalizeAddress(), expiry, tick, nonce);
	}

	public static string EncodeTradeCall(uint expiry, BigInteger size,
		BigInteger amount, int limitTick, uint deadline)
		=> EncodePagesCall("trade(bytes32[2])", expiry, size, amount,
			limitTick, deadline);

	public static string EncodePlaceCall(uint expiry, BigInteger size,
		BigInteger amount, int tick, uint deadline)
		=> EncodePagesCall("place(bytes32[2])", expiry, size, amount, tick,
			deadline);

	public static string EncodeCancelCall(uint expiry, IReadOnlyList<int> ticks,
		uint deadline)
	{
		ArgumentNullException.ThrowIfNull(ticks);
		if (ticks.Count is < 1 or > 8)
			throw new ArgumentOutOfRangeException(nameof(ticks),
				"SynFutures cancellation accepts one to eight ticks.");
		var encodedTicks = BigInteger.Zero;
		for (var i = 0; i < 8; i++)
		{
			var tick = i < ticks.Count ? ticks[i] : 8_388_607;
			encodedTicks += ToUnsigned(tick, 24) << (24 * i);
		}
		var value = ((BigInteger)deadline << 224) +
			(encodedTicks << 32) + expiry;
		return "0x" + AbiSelector("cancel(bytes32)") + AbiWord(value);
	}

	public static string AbiSelector(string signature)
		=> Convert.ToHexString(new Sha3Keccack().CalculateHash(
			Encoding.ASCII.GetBytes(signature)))[..8].ToLowerInvariant();

	public static string AbiTopic(string signature)
		=> "0x" + Convert.ToHexString(new Sha3Keccack().CalculateHash(
			Encoding.ASCII.GetBytes(signature))).ToLowerInvariant();

	public static string AbiWord(BigInteger value)
	{
		if (value < 0)
			value += BigInteger.One << 256;
		if (value < 0 || value >= BigInteger.One << 256)
			throw new ArgumentOutOfRangeException(nameof(value));
		return value.ToString("x", CultureInfo.InvariantCulture)
			.PadLeft(64, '0');
	}

	public static BigInteger ReadAbiWord(string data, int index,
		bool isSigned = false)
	{
		data = data.ThrowIfEmpty(nameof(data));
		if (data.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			data = data[2..];
		var offset = checked(index * 64);
		if (offset < 0 || data.Length < offset + 64)
			throw new InvalidDataException(
				"SynFutures event data is shorter than expected.");
		var result = BigInteger.Parse("0" + data.Substring(offset, 64),
			NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		if (isSigned && result >= BigInteger.One << 255)
			result -= BigInteger.One << 256;
		return result;
	}

	public static string ToRpcHex(this BigInteger value)
	{
		if (value < 0)
			throw new ArgumentOutOfRangeException(nameof(value));
		return "0x" + value.ToString("x", CultureInfo.InvariantCulture);
	}

	private static string EncodePagesCall(string signature, uint expiry,
		BigInteger size, BigInteger amount, int tick, uint deadline)
	{
		if (size < _minimumInt128 || size > _maximumInt128 ||
			amount < _minimumInt128 || amount > _maximumInt128)
			throw new ArgumentOutOfRangeException(nameof(size),
				"SynFutures size and margin must fit int128.");
		var page0 = ((BigInteger)deadline << 56) +
			(ToUnsigned(tick, 24) << 32) + expiry;
		var page1 = (ToUnsigned(size, 128) << 128) +
			ToUnsigned(amount, 128);
		return "0x" + AbiSelector(signature) + AbiWord(page0) +
			AbiWord(page1);
	}

	private static BigInteger ToUnsigned(BigInteger value, int bits)
	{
		var modulus = bits == 128 ? _two128 : _two24;
		var minimum = -(modulus >> 1);
		var maximum = (modulus >> 1) - 1;
		if (value < minimum || value > maximum)
			throw new ArgumentOutOfRangeException(nameof(value));
		return value < 0 ? modulus + value : value;
	}
}
