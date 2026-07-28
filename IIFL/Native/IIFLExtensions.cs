namespace StockSharp.IIFL.Native;

static class IIFLExtensions
{
	private static readonly TimeSpan _indiaOffset =
		TimeSpan.FromMinutes(330);

	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "1 minute",
			[TimeSpan.FromMinutes(5)] = "5 minutes",
			[TimeSpan.FromMinutes(10)] = "10 minutes",
			[TimeSpan.FromMinutes(15)] = "15 minutes",
			[TimeSpan.FromMinutes(30)] = "30 minutes",
			[TimeSpan.FromHours(1)] = "60 minutes",
			[TimeSpan.FromDays(1)] = "1 day",
			[TimeSpan.FromDays(7)] = "weekly",
			[TimeSpan.FromDays(30)] = "monthly",
		};

	public static readonly string[] Exchanges =
	[
		"NSEEQ",
		"BSEEQ",
		"NSEFO",
		"BSEFO",
		"NSECURR",
		"BSECURR",
		"MCXCOMM",
		"NSECOMM",
		"INDICES",
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToIIFLInterval(this TimeSpan value)
		=> _timeFrames.TryGetValue(value, out var interval)
			? interval
			: throw new NotSupportedException(
				$"IIFL candle interval '{value}' is unsupported.");

	public static string ToBoardCode(this string exchange)
		=> exchange?.Trim().ToUpperInvariant() switch
		{
			"NSEEQ" => BoardCodes.Nse,
			"BSEEQ" => BoardCodes.Bse,
			"NSEFO" => "NFO",
			"BSEFO" => "BFO",
			"NSECURR" => "CDS",
			"BSECURR" => "BCD",
			"MCXCOMM" => "MCX",
			"NSECOMM" => "NCO",
			"NCDEXCOMM" => "NCDEX",
			"BSECOMM" => "BSECOMM",
			_ => throw new ArgumentOutOfRangeException(
				nameof(exchange), exchange,
				"Unsupported IIFL exchange segment."),
		};

	public static string ToIIFLExchange(this string board)
		=> board?.Trim().ToUpperInvariant() switch
		{
			"NSE" => "NSEEQ",
			"BSE" => "BSEEQ",
			"NFO" => "NSEFO",
			"BFO" => "BSEFO",
			"CDS" => "NSECURR",
			"BCD" => "BSECURR",
			"MCX" => "MCXCOMM",
			"NCO" => "NSECOMM",
			"NCDEX" => "NCDEXCOMM",
			"BSECOMM" => "BSECOMM",
			_ => throw new ArgumentOutOfRangeException(
				nameof(board), board,
				"Unsupported IIFL board."),
		};

	public static SecurityTypes ToSecurityType(
		this IIFLInstrument instrument)
	{
		var type = instrument.InstrumentType?.Trim().ToUpperInvariant();
		if (type == "INDEX")
			return SecurityTypes.Index;
		if (type?.Contains("OPT", StringComparison.Ordinal) == true ||
			instrument.OptionType is "CE" or "PE")
			return SecurityTypes.Option;
		if (type?.Contains("FUT", StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"CE" => OptionTypes.Call,
			"PE" => OptionTypes.Put,
			_ => null,
		};

	public static SecurityId ToSecurityId(this IIFLInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol
				.IsEmpty(instrument.UnderlyingSymbol)
				.IsEmpty(instrument.InstrumentId),
			BoardCode = instrument.Exchange.ToBoardCode(),
			Native = $"{instrument.Exchange}/{instrument.InstrumentId}",
		};

	public static IIFLInstrumentRef ToReference(
		this IIFLInstrument instrument)
		=> new(
			instrument.Exchange,
			instrument.InstrumentId,
			instrument.TradingSymbol
				.IsEmpty(instrument.UnderlyingSymbol)
				.IsEmpty(instrument.InstrumentId),
			instrument.Exchange.ToBoardCode(),
			instrument.Lot);

	public static bool TryParseIIFLNative(this object value,
		out string exchange, out string instrumentId)
	{
		exchange = null;
		instrumentId = null;
		var text = value?.ToString();
		if (text.IsEmpty())
			return false;
		var separator = text.IndexOf('/');
		if (separator <= 0 || separator == text.Length - 1)
			separator = text.IndexOf(':');
		if (separator <= 0 || separator == text.Length - 1)
			return false;
		exchange = text[..separator].Trim().ToUpperInvariant();
		instrumentId = text[(separator + 1)..].Trim();
		return !exchange.IsEmpty() && !instrumentId.IsEmpty();
	}

	public static decimal? ToIIFLDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any,
			CultureInfo.InvariantCulture, out var result)
				? result
				: null;

	public static decimal? ToIIFLDecimal(this JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return value.Value<string>().ToIIFLDecimal();
	}

	public static DateTimeOffset ToIIFLTime(this JToken value,
		DateTimeOffset fallback)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return fallback;
		if (value.Type == JTokenType.Date)
		{
			var date = value.Value<DateTime>();
			return date.Kind == DateTimeKind.Unspecified
				? new(date, _indiaOffset)
				: new DateTimeOffset(date).ToOffset(_indiaOffset);
		}
		if (value.Type is JTokenType.Integer)
		{
			var number = value.Value<long>();
			return number > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(number)
				: DateTimeOffset.FromUnixTimeSeconds(number);
		}
		var text = value.Value<string>();
		if (text.IsEmpty())
			return fallback;
		if (long.TryParse(text, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var timestamp))
			return timestamp > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
				: DateTimeOffset.FromUnixTimeSeconds(timestamp);
		var formats = new[]
		{
			"dd-MMM-yyyy HH:mm:ss",
			"dd-MMM-yyyy HH:mm",
			"dd-MMM-yyyy",
			"dd-MM-yyyy HH:mm:ss",
			"dd/MM/yyyy HH:mm:ss",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd'T'HH:mm:ss",
		};
		if (DateTime.TryParseExact(text.Trim(), formats,
			CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces,
			out var local))
			return new(DateTime.SpecifyKind(local,
				DateTimeKind.Unspecified), _indiaOffset);
		if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var parsed))
			return parsed;
		return fallback;
	}

	public static JToken UnwrapIIFLResult(this JToken value)
	{
		if (value is not JObject obj)
			return value;
		return obj.Get("result") ?? obj.Get("data") ?? value;
	}

	public static JObject[] ToIIFLObjects(this JToken value)
	{
		value = value.UnwrapIIFLResult();
		if (value is JArray array)
			return array.OfType<JObject>().ToArray();
		if (value is JObject obj)
		{
			foreach (var name in new[]
				{
					"orders", "trades", "holdings", "positions",
					"candles", "data", "result",
				})
			{
				var nested = obj.Get(name);
				if (nested is JArray nestedArray)
					return nestedArray.OfType<JObject>().ToArray();
			}
			return [obj];
		}
		return [];
	}

	public static IIFLOrder ToIIFLOrder(this JObject value)
	{
		var volume = value.Decimal("quantity", "orderQuantity") ?? 0;
		var filled = value.Decimal("filledQuantity", "filledQty") ?? 0;
		return new()
		{
			OrderId = value.String("brokerOrderId", "orderId"),
			ExchangeOrderId = value.String("exchangeOrderId"),
			InstrumentId = value.String("instrumentId"),
			Symbol = value.String("tradingSymbol",
				"formattedInstrumentName"),
			Exchange = value.String("exchange"),
			Side = value.String("transactionType", "side")
				.EqualsIgnoreCase("SELL")
					? Sides.Sell
					: Sides.Buy,
			Product = value.String("product"),
			Complexity = value.String("orderComplexity"),
			Type = value.String("orderType"),
			Price = value.Decimal("price") ?? 0,
			AveragePrice = value.Decimal("averageTradedPrice",
				"averagePrice") ?? 0,
			TriggerPrice = value.Decimal("slTriggerPrice",
				"triggerPrice") ?? 0,
			Volume = volume,
			FilledVolume = filled,
			Balance = value.Decimal("pendingQuantity",
				"remainingQuantity") ?? Math.Max(0, volume - filled),
			Status = value.String("orderStatus", "status"),
			Time = value.Get("exchangeUpdateTime", "brokerUpdateTime",
				"exchangeTimestamp", "requestTime", "timestamp")
				.ToIIFLTime(DateTimeOffset.UtcNow),
			Error = value.String("rejectionReason", "message"),
			Tag = value.String("orderTag"),
		};
	}

	public static IIFLTrade ToIIFLTrade(this JObject value)
		=> new()
		{
			Id = value.String("tradeId", "exchangeTradeId",
				"brokerTradeId"),
			OrderId = value.String("brokerOrderId", "orderId"),
			InstrumentId = value.String("instrumentId"),
			Symbol = value.String("tradingSymbol",
				"formattedInstrumentName"),
			Exchange = value.String("exchange"),
			Side = value.String("transactionType", "side")
				.EqualsIgnoreCase("SELL")
					? Sides.Sell
					: Sides.Buy,
			Price = value.Decimal("tradePrice", "tradedPrice",
				"price") ?? 0,
			Volume = value.Decimal("tradeQuantity",
				"tradedQuantity", "quantity") ?? 0,
			Time = value.Get("tradeTimestamp", "exchangeTimestamp",
				"tradeTime", "timestamp")
				.ToIIFLTime(DateTimeOffset.UtcNow),
		};

	public static OrderStates ToIIFLOrderState(this string value)
	{
		var status = value?
			.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();
		if (status.IsEmpty())
			return OrderStates.Pending;
		if (status.Contains("REJECT", StringComparison.Ordinal) ||
			status.Contains("FAIL", StringComparison.Ordinal) ||
			status.Contains("ERROR", StringComparison.Ordinal))
			return OrderStates.Failed;
		if (status.Contains("PARTIAL", StringComparison.Ordinal))
			return OrderStates.Active;
		if (status.Contains("CANCEL", StringComparison.Ordinal) ||
			status.Contains("COMPLETE", StringComparison.Ordinal) ||
			status.Contains("FILLED", StringComparison.Ordinal) ||
			status.Contains("TRADED", StringComparison.Ordinal) ||
			status.Contains("EXPIRED", StringComparison.Ordinal))
			return OrderStates.Done;
		if (status.Contains("OPEN", StringComparison.Ordinal) ||
			status.Contains("PENDING", StringComparison.Ordinal) ||
			status.Contains("MODIFIED", StringComparison.Ordinal) ||
			status.Contains("TRIGGER", StringComparison.Ordinal))
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static OrderTypes ToIIFLOrderType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"SL" or "SLM" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static IIFLMarketFeed ParseMarketFeed(byte[] data)
	{
		if (data.Length < 188)
			throw new InvalidDataException(
				$"IIFL market-feed packet must contain 188 bytes, " +
					$"but contains {data.Length}.");
		var divisor = BinaryPrimitives.ReadInt32LittleEndian(
			data[58..62]);
		if (divisor <= 0)
			divisor = 1;
		decimal Price(int offset)
			=> BinaryPrimitives.ReadInt32LittleEndian(
				data[offset..(offset + 4)]) / (decimal)divisor;
		decimal Quantity(int offset)
			=> BinaryPrimitives.ReadUInt32LittleEndian(
				data[offset..(offset + 4)]);
		IIFLDepthLevel[] Levels(int offset)
			=> Enumerable.Range(0, 5)
				.Select(index =>
				{
					var current = offset + index * 12;
					return new IIFLDepthLevel(
						Price(current + 4),
						Quantity(current),
						BinaryPrimitives.ReadInt16LittleEndian(
							data[(current + 8)..(current + 10)]));
				})
				.Where(static level =>
					level.Price > 0 && level.Volume > 0)
				.ToArray();
		var timestamp = BinaryPrimitives.ReadInt32LittleEndian(
			data[62..66]);
		return new()
		{
			LastPrice = Price(0),
			LastVolume = Quantity(4),
			Volume = Quantity(8),
			High = Price(12),
			Low = Price(16),
			Open = Price(20),
			Close = Price(24),
			AveragePrice = Price(28),
			BestBidVolume = Quantity(34),
			BestBidPrice = Price(38),
			BestAskVolume = Quantity(42),
			BestAskPrice = Price(46),
			TotalBidVolume = Quantity(50),
			TotalAskVolume = Quantity(54),
			Time = timestamp > 0
				? DateTimeOffset.FromUnixTimeSeconds(timestamp)
				: DateTimeOffset.UtcNow,
			Bids = Levels(66),
			Asks = Levels(126),
		};
	}

	public static IIFLOpenInterest ParseOpenInterest(byte[] data)
	{
		if (data.Length < 16)
			throw new InvalidDataException(
				"IIFL open-interest packet must contain 16 bytes.");
		return new(
			BinaryPrimitives.ReadInt32LittleEndian(data[0..4]),
			BinaryPrimitives.ReadInt32LittleEndian(data[4..8]),
			BinaryPrimitives.ReadInt32LittleEndian(data[8..12]),
			BinaryPrimitives.ReadInt32LittleEndian(data[12..16]));
	}

	public static JToken FindIIFL(this JObject value,
		params string[] names)
		=> value.Get(names);

	public static string FindIIFLString(this JObject value,
		params string[] names)
		=> value.String(names);

	public static decimal? FindIIFLDecimal(this JObject value,
		params string[] names)
		=> value.Decimal(names);

	private static JToken Get(this JObject value, params string[] names)
	{
		if (value is null)
			return null;
		foreach (var name in names)
		{
			var token = value.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is not null &&
				token.Type is not JTokenType.Null and
					not JTokenType.Undefined)
				return token;
		}
		return null;
	}

	private static string String(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<string>();

	private static decimal? Decimal(this JObject value,
		params string[] names)
		=> value.Get(names).ToIIFLDecimal();
}
