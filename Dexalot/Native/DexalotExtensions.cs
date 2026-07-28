namespace StockSharp.Dexalot.Native;

static class DexalotExtensions
{
	public const string ProbeAddress =
		"0x000000000000000000000000000000000000dEaD";

	public static SecurityId ToStockSharp(this DexalotPair pair)
	{
		ArgumentNullException.ThrowIfNull(pair);
		return new()
		{
			SecurityCode = pair.Pair.ToUpperInvariant(),
			BoardCode = BoardCodes.Dexalot,
		};
	}

	public static decimal ParseDecimal(this string value, string field)
	{
		if (!decimal.TryParse(value, NumberStyles.Number,
			CultureInfo.InvariantCulture, out var result))
			throw new InvalidDataException(
				$"Dexalot field '{field}' contains invalid decimal '{value}'.");
		return result;
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
				NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
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

	public static BigInteger ToBaseUnits(this decimal value, int decimals)
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
		var result = BigInteger.DivRem(unscaled, divisor, out var remainder);
		if (remainder != 0)
			throw new ArgumentException(
				$"Value '{value}' exceeds the supported {decimals}-decimal " +
					"precision.", nameof(value));
		return result;
	}

	public static decimal FromBaseUnits(this BigInteger value, int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		var negative = value.Sign < 0;
		var digits = BigInteger.Abs(value).ToString(
			CultureInfo.InvariantCulture);
		var text = decimals switch
		{
			0 => digits,
			_ when digits.Length <= decimals =>
				"0." + new string('0', decimals - digits.Length) + digits,
			_ => digits.Insert(digits.Length - decimals, "."),
		};
		return decimal.Parse((negative ? "-" : string.Empty) + text,
			NumberStyles.Number, CultureInfo.InvariantCulture);
	}

	public static decimal GetStep(this int decimals)
	{
		if (decimals is < 0 or > 28)
			throw new ArgumentOutOfRangeException(nameof(decimals));
		return 1m / (decimal)BigInteger.Pow(10, decimals);
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
				$"Invalid EVM hash '{hash}'.", nameof(hash));
		return "0x" + hash[2..].ToLowerInvariant();
	}

	public static string AbiWord(BigInteger value)
	{
		if (value < 0 || value >= BigInteger.Pow(2, 256))
			throw new ArgumentOutOfRangeException(nameof(value));
		return value.ToString("x", CultureInfo.InvariantCulture)
			.PadLeft(64, '0');
	}

	public static string AbiAddress(string address)
		=> address.NormalizeAddress()[2..].PadLeft(64, '0').ToLowerInvariant();

	public static string AbiBytes32(string value)
	{
		var bytes = Encoding.UTF8.GetBytes(
			value.ThrowIfEmpty(nameof(value)));
		if (bytes.Length > 32)
			throw new ArgumentException(
				"ABI bytes32 text cannot exceed 32 UTF-8 bytes.",
				nameof(value));
		return bytes.ToHex().PadRight(64, '0');
	}

	public static string EncodeCall(string signature, params string[] words)
	{
		var selector = new Sha3Keccack().CalculateHash(
			signature.ThrowIfEmpty(nameof(signature)))[..8];
		return "0x" + selector + string.Concat(words ?? []);
	}

	public static BigInteger ReadWord(string data, int index)
	{
		data = data.ThrowIfEmpty(nameof(data));
		if (data.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			data = data[2..];
		var start = checked(index * 64);
		if (index < 0 || start + 64 > data.Length)
			throw new InvalidDataException(
				$"ABI response does not contain word {index}.");
		return ("0x" + data.Substring(start, 64)).ParseInteger();
	}

	public static string ReadBytes32(string data, int index)
		=> "0x" + ReadRawWord(data, index).ToLowerInvariant();

	public static string ReadBytes32Text(string data, int index)
	{
		var bytes = ReadRawWord(data, index).HexToByteArray();
		var length = Array.IndexOf(bytes, (byte)0);
		if (length < 0)
			length = bytes.Length;
		return Encoding.UTF8.GetString(bytes, 0, length);
	}

	public static BigInteger[] ReadDynamicUIntArray(string data,
		int offsetWord)
	{
		var offset = ReadWord(data, offsetWord);
		if (offset < 0 || offset > int.MaxValue)
			throw new InvalidDataException("ABI array offset is invalid.");
		var startWord = checked((int)offset / 32);
		var count = ReadWord(data, startWord);
		if (count < 0 || count > 100_000)
			throw new InvalidDataException("ABI array length is invalid.");
		var result = new BigInteger[(int)count];
		for (var index = 0; index < result.Length; index++)
			result[index] = ReadWord(data, startWord + 1 + index);
		return result;
	}

	private static string ReadRawWord(string data, int index)
	{
		data = data.ThrowIfEmpty(nameof(data));
		if (data.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
			data = data[2..];
		var start = checked(index * 64);
		if (index < 0 || start + 64 > data.Length)
			throw new InvalidDataException(
				$"ABI response does not contain word {index}.");
		return data.Substring(start, 64);
	}

	public static CurrencyTypes? ToCurrency(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"USD" or "USDC" or "USDT" or "DAI" => CurrencyTypes.USD,
			"EUR" or "EURC" => CurrencyTypes.EUR,
			"BTC" or "WBTC" => CurrencyTypes.BTC,
			_ => System.Enum.TryParse<CurrencyTypes>(value, true,
				out var currency) ? currency : null,
		};

	public static Sides ToSide(this JToken value)
	{
		var text = value?.ToString();
		return text?.Trim().ToUpperInvariant() switch
		{
			"0" or "BUY" => Sides.Buy,
			"1" or "SELL" => Sides.Sell,
			_ => throw new InvalidDataException(
				$"Unknown Dexalot side '{text}'."),
		};
	}

	public static OrderStates ToOrderState(this JToken value)
	{
		var text = value?.ToString()?.Trim().ToUpperInvariant();
		return text switch
		{
			"0" or "NEW" or "2" or "PARTIAL" => OrderStates.Active,
			"3" or "FILLED" or "4" or "CANCELED" or "5" or "EXPIRED" or
				"6" or "KILLED" => OrderStates.Done,
			"1" or "REJECTED" => OrderStates.Failed,
			_ => throw new InvalidDataException(
				$"Unknown Dexalot order status '{text}'."),
		};
	}

	public static OrderTypes ToOrderType(this JToken value)
		=> value?.ToString()?.Trim().ToUpperInvariant() switch
		{
			"0" or "MARKET" => OrderTypes.Market,
			"1" or "LIMIT" => OrderTypes.Limit,
			_ => OrderTypes.Conditional,
		};

	public static TimeInForce? ToTimeInForce(this JToken value)
		=> value?.ToString()?.Trim().ToUpperInvariant() switch
		{
			"1" or "FOK" => TimeInForce.MatchOrCancel,
			"2" or "IOC" => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static int ToDexalotType2(this OrderRegisterMessage message)
		=> message.PostOnly == true
			? 3
			: message.TimeInForce switch
			{
				TimeInForce.MatchOrCancel => 1,
				TimeInForce.CancelBalance => 2,
				_ => 0,
			};

	public static string ToChartCode(this TimeSpan timeFrame)
		=> timeFrame switch
		{
			{ TotalMinutes: 5 } => "M5",
			{ TotalMinutes: 15 } => "M15",
			{ TotalMinutes: 30 } => "M30",
			{ TotalHours: 1 } => "H1",
			{ TotalHours: 4 } => "H4",
			{ TotalDays: 1 } => "D1",
			_ => throw new NotSupportedException(
				$"Dexalot does not support the {timeFrame} candle interval."),
		};
}
