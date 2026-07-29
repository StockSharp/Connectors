namespace StockSharp.Intrinio.Native;

enum IntrinioEquityQuoteTypes
{
	Ask = 1,
	Bid = 2,
}

sealed record IntrinioEquityTrade(
	string Symbol,
	double Price,
	uint Size,
	ulong TotalVolume,
	DateTime Timestamp,
	byte SubProvider,
	char MarketCenter,
	string Condition);

sealed record IntrinioEquityQuote(
	IntrinioEquityQuoteTypes Type,
	string Symbol,
	double Price,
	uint Size,
	DateTime Timestamp,
	byte SubProvider,
	char MarketCenter,
	string Condition);

sealed record IntrinioOptionTrade(
	string Contract,
	double Price,
	uint Size,
	double Timestamp,
	ulong TotalVolume,
	double AskPriceAtExecution,
	double BidPriceAtExecution,
	double UnderlyingPriceAtExecution,
	byte[] Qualifiers,
	char Exchange);

sealed record IntrinioOptionQuote(
	string Contract,
	double AskPrice,
	uint AskSize,
	double BidPrice,
	uint BidSize,
	double Timestamp);

sealed record IntrinioOptionRefresh(
	string Contract,
	uint OpenInterest,
	double OpenPrice,
	double ClosePrice,
	double HighPrice,
	double LowPrice);

enum IntrinioDecodedEventTypes
{
	EquityTrade,
	EquityQuote,
	OptionTrade,
	OptionQuote,
	OptionRefresh,
}

sealed class IntrinioDecodedEvent
{
	private IntrinioDecodedEvent(IntrinioDecodedEventTypes type)
	{
		Type = type;
	}

	public IntrinioDecodedEventTypes Type { get; }
	public IntrinioEquityTrade EquityTrade { get; private init; }
	public IntrinioEquityQuote EquityQuote { get; private init; }
	public IntrinioOptionTrade OptionTrade { get; private init; }
	public IntrinioOptionQuote OptionQuote { get; private init; }
	public IntrinioOptionRefresh OptionRefresh { get; private init; }

	public string Symbol => Type switch
	{
		IntrinioDecodedEventTypes.EquityTrade => EquityTrade.Symbol,
		IntrinioDecodedEventTypes.EquityQuote => EquityQuote.Symbol,
		IntrinioDecodedEventTypes.OptionTrade => OptionTrade.Contract,
		IntrinioDecodedEventTypes.OptionQuote => OptionQuote.Contract,
		IntrinioDecodedEventTypes.OptionRefresh => OptionRefresh.Contract,
		_ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null),
	};

	public bool IsOption => Type is IntrinioDecodedEventTypes.OptionTrade or
		IntrinioDecodedEventTypes.OptionQuote or IntrinioDecodedEventTypes.OptionRefresh;

	public static IntrinioDecodedEvent From(IntrinioEquityTrade value)
		=> new(IntrinioDecodedEventTypes.EquityTrade) { EquityTrade = value };

	public static IntrinioDecodedEvent From(IntrinioEquityQuote value)
		=> new(IntrinioDecodedEventTypes.EquityQuote) { EquityQuote = value };

	public static IntrinioDecodedEvent From(IntrinioOptionTrade value)
		=> new(IntrinioDecodedEventTypes.OptionTrade) { OptionTrade = value };

	public static IntrinioDecodedEvent From(IntrinioOptionQuote value)
		=> new(IntrinioDecodedEventTypes.OptionQuote) { OptionQuote = value };

	public static IntrinioDecodedEvent From(IntrinioOptionRefresh value)
		=> new(IntrinioDecodedEventTypes.OptionRefresh) { OptionRefresh = value };
}

static class IntrinioRealtimeProtocol
{
	private const string _clientInformation = "StockSharp.Intrinio/1.0";
	private const string _firehose = "$FIREHOSE";
	private const int _maxEquityChunkLength = 86;
	private const int _optionContractSlotLength = 21;
	private const int _optionMessageTypeOffset = 22;
	private const int _optionTradeLength = 72;
	private const int _optionQuoteLength = 52;
	private const int _optionRefreshLength = 52;
	private const int _optionUnusualActivityLength = 74;

	private static readonly Encoding _ascii = Encoding.GetEncoding(
		Encoding.ASCII.CodePage,
		EncoderFallback.ExceptionFallback,
		DecoderFallback.ExceptionFallback);

	public static Uri GetEquityAuthUri(IntrinioEquityProviders provider, string apiKey)
		=> new($"https://{GetEquityHost(provider)}/auth?api_key={EscapeQueryValue(apiKey, nameof(apiKey))}");

	public static Uri GetEquityWebSocketUri(IntrinioEquityProviders provider, string token)
		=> new($"wss://{GetEquityHost(provider)}/socket/websocket?vsn=1.0.0&token={EscapeQueryValue(token, nameof(token))}" +
			(provider == IntrinioEquityProviders.DelayedSip ? "&delayed=true" : string.Empty));

	public static Uri GetOptionsAuthUri(IntrinioOptionProviders provider, string apiKey)
		=> new($"https://{GetOptionsHost(provider)}/auth?api_key={EscapeQueryValue(apiKey, nameof(apiKey))}");

	public static Uri GetOptionsWebSocketUri(IntrinioOptionProviders provider,
		string token, bool delayed)
		=> new($"wss://{GetOptionsHost(provider)}/socket/websocket?vsn=1.0.0&token={EscapeQueryValue(token, nameof(token))}" +
			(delayed ? "&delayed=true" : string.Empty));

	public static IReadOnlyDictionary<string, string> GetEquityAuthHeaders()
		=> new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Client-Information"] = _clientInformation,
			["UseNewEquitiesFormat"] = "v2",
		};

	public static IReadOnlyDictionary<string, string> GetEquityWebSocketHeaders()
		=> new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["UseNewEquitiesFormat"] = "v2",
		};

	public static IReadOnlyDictionary<string, string> GetOptionsAuthHeaders(bool delayed)
		=> GetOptionsHeaders(delayed, true);

	public static IReadOnlyDictionary<string, string> GetOptionsWebSocketHeaders(bool delayed)
		=> GetOptionsHeaders(delayed, false);

	public static byte[] EncodeEquityJoin(string symbol, bool tradesOnly)
	{
		var bytes = EncodeSubscriptionSymbol(symbol);
		var result = new byte[2 + bytes.Length];
		result[0] = (byte)'J';
		result[1] = tradesOnly ? (byte)1 : (byte)0;
		bytes.CopyTo(result, 2);
		return result;
	}

	public static byte[] EncodeEquityLeave(string symbol)
	{
		var bytes = EncodeSubscriptionSymbol(symbol);
		var result = new byte[1 + bytes.Length];
		result[0] = (byte)'L';
		bytes.CopyTo(result, 1);
		return result;
	}

	public static byte[] EncodeOptionsJoin(string symbol, bool tradesOnly)
	{
		var bytes = EncodeOptionSubscriptionSymbol(symbol);
		var result = new byte[2 + bytes.Length];
		result[0] = (byte)'J';
		result[1] = tradesOnly ? (byte)0b101 : (byte)0b111;
		bytes.CopyTo(result, 2);
		return result;
	}

	public static byte[] EncodeOptionsLeave(string symbol)
	{
		var bytes = EncodeOptionSubscriptionSymbol(symbol);
		if (IsLobby(symbol))
		{
			var lobbyResult = new byte[1 + bytes.Length];
			lobbyResult[0] = (byte)'L';
			bytes.CopyTo(lobbyResult, 1);
			return lobbyResult;
		}

		var result = new byte[2 + bytes.Length];
		result[0] = (byte)'L';
		bytes.CopyTo(result, 2);
		return result;
	}

	public static IReadOnlyList<IntrinioDecodedEvent> DecodeEquity(
		ReadOnlySpan<byte> batch)
	{
		var count = ReadBatchCount(batch, "equity");
		var result = new List<IntrinioDecodedEvent>(count);
		var offset = 1;

		for (var i = 0; i < count; i++)
		{
			if (batch.Length - offset < 2)
				throw Invalid($"Equity batch event {i} has no complete header.");

			var type = batch[offset];
			var length = batch[offset + 1];
			if (type > (byte)IntrinioEquityQuoteTypes.Bid)
				throw Invalid($"Equity batch event {i} has unsupported type {type}.");
			if (length < 3 || length > _maxEquityChunkLength)
				throw Invalid($"Equity batch event {i} has invalid length {length}.");
			if (batch.Length - offset < length)
				throw Invalid($"Equity batch event {i} is truncated.");

			var chunk = batch.Slice(offset, length);
			result.Add(type == 0
				? IntrinioDecodedEvent.From(DecodeEquityTrade(chunk, i))
				: IntrinioDecodedEvent.From(DecodeEquityQuote(chunk, i)));
			offset += length;
		}

		EnsureFullyConsumed(batch, offset, "Equity");
		return result;
	}

	public static IReadOnlyList<IntrinioDecodedEvent> DecodeOptions(
		ReadOnlySpan<byte> batch)
	{
		var count = ReadBatchCount(batch, "options");
		var result = new List<IntrinioDecodedEvent>(count);
		var offset = 1;

		for (var i = 0; i < count; i++)
		{
			if (batch.Length - offset <= _optionMessageTypeOffset)
				throw Invalid($"Options batch event {i} has no complete header.");

			var type = batch[offset + _optionMessageTypeOffset];
			var length = type switch
			{
				0 => _optionTradeLength,
				1 => _optionQuoteLength,
				2 => _optionRefreshLength,
				>= 3 and <= 6 => _optionUnusualActivityLength,
				_ => throw Invalid($"Options batch event {i} has unsupported type {type}."),
			};

			if (batch.Length - offset < length)
				throw Invalid($"Options batch event {i} is truncated.");

			var chunk = batch.Slice(offset, length);
			var contract = DecodeOptionContract(chunk, i);
			switch (type)
			{
				case 0:
					result.Add(IntrinioDecodedEvent.From(
						DecodeOptionTrade(chunk, contract, i)));
					break;
				case 1:
					result.Add(IntrinioDecodedEvent.From(
						DecodeOptionQuote(chunk, contract, i)));
					break;
				case 2:
					result.Add(IntrinioDecodedEvent.From(
						DecodeOptionRefresh(chunk, contract, i)));
					break;
			}

			offset += length;
		}

		EnsureFullyConsumed(batch, offset, "Options");
		return result;
	}

	private static IntrinioEquityTrade DecodeEquityTrade(
		ReadOnlySpan<byte> chunk, int index)
	{
		var symbolLength = chunk[2];
		var conditionOffset = 26 + symbolLength;
		if (conditionOffset >= chunk.Length)
			throw Invalid($"Equity trade {index} has an invalid symbol length.");

		var conditionLength = chunk[conditionOffset];
		var expectedLength = 27 + symbolLength + conditionLength;
		if (chunk.Length != expectedLength)
			throw Invalid($"Equity trade {index} length does not match its fields.");

		var symbol = DecodeAscii(chunk.Slice(3, symbolLength),
			$"equity trade {index} symbol");
		ValidateSymbol(symbol, $"equity trade {index}");
		var subProvider = chunk[3 + symbolLength];
		var marketCenter = DecodeMarketCenter(chunk.Slice(4 + symbolLength, 2));
		var price = DecodeSingle(chunk.Slice(6 + symbolLength, 4));
		var size = BinaryPrimitives.ReadUInt32LittleEndian(
			chunk.Slice(10 + symbolLength, 4));
		var timestamp = DecodeEquityTimestamp(
			BinaryPrimitives.ReadUInt64LittleEndian(
				chunk.Slice(14 + symbolLength, 8)), index);
		var totalVolume = BinaryPrimitives.ReadUInt32LittleEndian(
			chunk.Slice(22 + symbolLength, 4));
		var condition = DecodeAscii(
			chunk.Slice(27 + symbolLength, conditionLength),
			$"equity trade {index} condition");

		return new(symbol, price, size, totalVolume, timestamp,
			subProvider, marketCenter, condition);
	}

	private static IntrinioEquityQuote DecodeEquityQuote(
		ReadOnlySpan<byte> chunk, int index)
	{
		var symbolLength = chunk[2];
		var conditionOffset = 22 + symbolLength;
		if (conditionOffset >= chunk.Length)
			throw Invalid($"Equity quote {index} has an invalid symbol length.");

		var conditionLength = chunk[conditionOffset];
		var expectedLength = 23 + symbolLength + conditionLength;
		if (chunk.Length != expectedLength)
			throw Invalid($"Equity quote {index} length does not match its fields.");

		var symbol = DecodeAscii(chunk.Slice(3, symbolLength),
			$"equity quote {index} symbol");
		ValidateSymbol(symbol, $"equity quote {index}");
		var subProvider = chunk[3 + symbolLength];
		var marketCenter = DecodeMarketCenter(chunk.Slice(4 + symbolLength, 2));
		var price = DecodeSingle(chunk.Slice(6 + symbolLength, 4));
		var size = BinaryPrimitives.ReadUInt32LittleEndian(
			chunk.Slice(10 + symbolLength, 4));
		var timestamp = DecodeEquityTimestamp(
			BinaryPrimitives.ReadUInt64LittleEndian(
				chunk.Slice(14 + symbolLength, 8)), index);
		var condition = DecodeAscii(
			chunk.Slice(23 + symbolLength, conditionLength),
			$"equity quote {index} condition");

		return new((IntrinioEquityQuoteTypes)chunk[0], symbol, price,
			size, timestamp, subProvider, marketCenter, condition);
	}

	private static IntrinioOptionTrade DecodeOptionTrade(
		ReadOnlySpan<byte> chunk, string contract, int index)
	{
		var priceType = ValidateOptionPriceType(chunk[23], index, "price");
		var underlyingPriceType =
			ValidateOptionPriceType(chunk[24], index, "underlying price");
		var qualifiers = chunk.Slice(61, 4).ToArray();
		var exchange = chunk[65];

		return new(
			contract,
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(25, 4)), priceType),
			BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(29, 4)),
			ScaleOptionTimestamp(BinaryPrimitives.ReadUInt64LittleEndian(
				chunk.Slice(33, 8))),
			BinaryPrimitives.ReadUInt64LittleEndian(chunk.Slice(41, 8)),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(49, 4)), priceType),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(53, 4)), priceType),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(57, 4)), underlyingPriceType),
			qualifiers,
			(char)exchange);
	}

	private static IntrinioOptionQuote DecodeOptionQuote(
		ReadOnlySpan<byte> chunk, string contract, int index)
	{
		var priceType = ValidateOptionPriceType(chunk[23], index, "price");
		return new(
			contract,
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(24, 4)), priceType),
			BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(28, 4)),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(32, 4)), priceType),
			BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(36, 4)),
			ScaleOptionTimestamp(BinaryPrimitives.ReadUInt64LittleEndian(
				chunk.Slice(40, 8))));
	}

	private static IntrinioOptionRefresh DecodeOptionRefresh(
		ReadOnlySpan<byte> chunk, string contract, int index)
	{
		var priceType = ValidateOptionPriceType(chunk[23], index, "price");
		return new(
			contract,
			BinaryPrimitives.ReadUInt32LittleEndian(chunk.Slice(24, 4)),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(28, 4)), priceType),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(32, 4)), priceType),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(36, 4)), priceType),
			ScaleOptionPrice(BinaryPrimitives.ReadInt32LittleEndian(
				chunk.Slice(40, 4)), priceType));
	}

	private static string DecodeOptionContract(ReadOnlySpan<byte> chunk, int index)
	{
		var length = chunk[0];
		if (length is 0 or > _optionContractSlotLength)
			throw Invalid($"Options batch event {index} has invalid contract length {length}.");

		var wireContract = DecodeAscii(chunk.Slice(1, length),
			$"options batch event {index} contract");
		var separator = wireContract.IndexOf('_');
		if (separator is < 1 or > 6)
			throw Invalid($"Options batch event {index} has a malformed contract.");

		var root = wireContract[..separator];
		if (root.Any(character => !char.IsLetterOrDigit(character) &&
			character is not '.' and not '-'))
		{
			throw Invalid($"Options batch event {index} has a malformed contract root.");
		}

		var suffix = wireContract[(separator + 1)..];
		if (suffix.Length < 10 ||
			suffix[6] is not ('C' or 'P') ||
			!suffix.AsSpan(0, 6).ToArray().All(char.IsAsciiDigit))
		{
			throw Invalid($"Options batch event {index} has a malformed contract suffix.");
		}

		if (!DateTime.TryParseExact(suffix[..6], "yyMMdd",
			CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
		{
			throw Invalid($"Options batch event {index} has an invalid contract date.");
		}

		var decimalOffset = suffix.IndexOf('.', 7);
		if (decimalOffset < 8)
			throw Invalid($"Options batch event {index} has a malformed strike.");

		var whole = suffix[7..decimalOffset];
		var fractional = suffix[(decimalOffset + 1)..];
		if (whole.Length is < 1 or > 5 ||
			fractional.Length is < 1 or > 4 ||
			!whole.All(char.IsAsciiDigit) ||
			!fractional.All(char.IsAsciiDigit))
		{
			throw Invalid($"Options batch event {index} has a malformed strike.");
		}

		var normalizedFraction = fractional[..Math.Min(3, fractional.Length)]
			.PadRight(3, '0');
		return root.PadRight(6, '_') + suffix[..7] +
			whole.PadLeft(5, '0') + normalizedFraction;
	}

	private static byte ValidateOptionPriceType(byte priceType,
		int index, string field)
	{
		if (priceType <= 15)
			return priceType;
		throw Invalid($"Options batch event {index} has invalid {field} type {priceType}.");
	}

	private static double ScaleOptionPrice(int value, byte priceType)
	{
		if (value is int.MaxValue or int.MinValue || priceType == 15)
			return double.NaN;

		var divisor = priceType switch
		{
			0 => 1d,
			1 => 10d,
			2 => 100d,
			3 => 1_000d,
			4 => 10_000d,
			5 => 100_000d,
			6 => 1_000_000d,
			7 => 10_000_000d,
			8 => 100_000_000d,
			9 => 1_000_000_000d,
			10 => 512d,
			>= 11 and <= 14 => 0d,
			15 => double.NaN,
			_ => throw new ArgumentOutOfRangeException(nameof(priceType), priceType, null),
		};
		return value / divisor;
	}

	private static double ScaleOptionTimestamp(ulong nanoseconds)
		=> nanoseconds / 1_000_000_000d;

	private static DateTime DecodeEquityTimestamp(ulong nanoseconds, int index)
	{
		var ticks = nanoseconds / 100;
		if (ticks > (ulong)(DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks))
			throw Invalid($"Equity batch event {index} has an out-of-range timestamp.");
		return DateTime.UnixEpoch.AddTicks((long)ticks);
	}

	private static double DecodeSingle(ReadOnlySpan<byte> bytes)
		=> BitConverter.Int32BitsToSingle(
			BinaryPrimitives.ReadInt32LittleEndian(bytes));

	private static char DecodeMarketCenter(ReadOnlySpan<byte> bytes)
		=> (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes);

	private static int ReadBatchCount(ReadOnlySpan<byte> batch, string feed)
	{
		if (batch.IsEmpty)
			throw Invalid($"The {feed} batch is empty.");
		return batch[0];
	}

	private static void EnsureFullyConsumed(ReadOnlySpan<byte> batch,
		int offset, string feed)
	{
		if (offset != batch.Length)
			throw Invalid($"{feed} batch has {batch.Length - offset} trailing bytes.");
	}

	private static string DecodeAscii(ReadOnlySpan<byte> bytes, string field)
	{
		try
		{
			return _ascii.GetString(bytes);
		}
		catch (DecoderFallbackException error)
		{
			throw Invalid($"{field} is not ASCII.", error);
		}
	}

	private static byte[] EncodeSubscriptionSymbol(string symbol)
	{
		symbol = Require(symbol, nameof(symbol));
		return EncodeAscii(IsLobby(symbol) ? _firehose : symbol, nameof(symbol));
	}

	private static byte[] EncodeOptionSubscriptionSymbol(string symbol)
	{
		symbol = Require(symbol, nameof(symbol));
		if (IsLobby(symbol))
			return EncodeAscii(_firehose, nameof(symbol));
		return EncodeAscii(TranslateOptionContract(symbol), nameof(symbol));
	}

	private static string TranslateOptionContract(string contract)
	{
		EncodeAscii(contract, nameof(contract));
		if (contract.Length <= 9 || contract.IndexOf('.') >= 9)
			return contract;

		if (!IntrinioOptionKey.TryParse(contract, out var key))
			throw new ArgumentException(
				$"Invalid Intrinio option contract '{contract}'.", nameof(contract));

		var standard = key.StreamCode;
		var symbol = standard[..6].TrimEnd('_');
		var whole = standard.Substring(13, 5).TrimStart('0');
		if (whole.Length == 0)
			whole = "0";
		var fractional = standard[18..];
		if (fractional[2] == '0')
			fractional = fractional[..2];
		return $"{symbol}_{standard.Substring(6, 6)}{standard[12]}{whole}.{fractional}";
	}

	private static byte[] EncodeAscii(string value, string parameterName)
	{
		try
		{
			return _ascii.GetBytes(value);
		}
		catch (EncoderFallbackException error)
		{
			throw new ArgumentException(
				$"The value must contain ASCII characters only.", parameterName, error);
		}
	}

	private static void ValidateSymbol(string symbol, string field)
	{
		if (symbol.Length == 0 || symbol.Any(character => char.IsControl(character)))
			throw Invalid($"{field} has an invalid symbol.");
	}

	private static bool IsLobby(string symbol)
		=> symbol.Equals("lobby", StringComparison.Ordinal);

	private static string GetEquityHost(IntrinioEquityProviders provider)
		=> provider switch
		{
			IntrinioEquityProviders.Realtime or IntrinioEquityProviders.Iex =>
				"realtime-mx.intrinio.com",
			IntrinioEquityProviders.DelayedSip =>
				"realtime-delayed-sip.intrinio.com",
			IntrinioEquityProviders.NasdaqBasic =>
				"realtime-nasdaq-basic.intrinio.com",
			IntrinioEquityProviders.CboeOne =>
				"cboe-one.intrinio.com",
			IntrinioEquityProviders.EquitiesEdge =>
				"equities-edge.intrinio.com",
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	private static string GetOptionsHost(IntrinioOptionProviders provider)
		=> provider switch
		{
			IntrinioOptionProviders.Opra => "realtime-options.intrinio.com",
			IntrinioOptionProviders.OptionsEdge => "options-edge.intrinio.com",
			_ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
		};

	private static IReadOnlyDictionary<string, string> GetOptionsHeaders(
		bool delayed, bool includeClientInformation)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["UseNewOptionsFormat"] = "v2",
		};
		if (includeClientInformation)
			result["Client-Information"] = _clientInformation;
		if (delayed)
			result["delay"] = "true";
		return result;
	}

	private static string Require(string value, string parameterName)
	{
		if (string.IsNullOrWhiteSpace(value))
			throw new ArgumentException("The value cannot be empty.", parameterName);
		return value;
	}

	private static string EscapeQueryValue(string value, string parameterName)
		=> Uri.EscapeDataString(Require(value, parameterName));

	private static InvalidDataException Invalid(string message,
		Exception innerException = null)
		=> new(message, innerException);
}
