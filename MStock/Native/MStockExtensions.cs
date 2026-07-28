namespace StockSharp.MStock.Native;

static class MStockExtensions
{
	private static readonly TimeSpan _indiaOffset =
		TimeSpan.FromMinutes(330);
	private static readonly DateTimeOffset _streamEpoch =
		new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "ONE_MINUTE",
			[TimeSpan.FromMinutes(3)] = "THREE_MINUTE",
			[TimeSpan.FromMinutes(5)] = "FIVE_MINUTE",
			[TimeSpan.FromMinutes(10)] = "TEN_MINUTE",
			[TimeSpan.FromMinutes(15)] = "FIFTEEN_MINUTE",
			[TimeSpan.FromMinutes(30)] = "THIRTY_MINUTE",
			[TimeSpan.FromHours(1)] = "ONE_HOUR",
			[TimeSpan.FromDays(1)] = "ONE_DAY",
		};

	public static IEnumerable<TimeSpan> TimeFrames =>
		_timeFrames.Keys;

	public static string ToMStockInterval(this TimeSpan value)
		=> _timeFrames.TryGetValue(value, out var interval)
			? interval
			: throw new NotSupportedException(
				$"m.Stock candle interval '{value}' is unsupported.");

	public static int ToMStockExchangeType(this string exchange)
		=> exchange?.Trim().ToUpperInvariant() switch
		{
			"NSE" => 1,
			"NFO" => 2,
			"BSE" => 3,
			"BFO" => 4,
			"CDS" => 13,
			_ => throw new ArgumentOutOfRangeException(
				nameof(exchange), exchange,
				"Unsupported m.Stock exchange segment."),
		};

	public static string ToMStockExchange(this int value)
		=> value switch
		{
			1 => "NSE",
			2 => "NFO",
			3 => "BSE",
			4 => "BFO",
			13 => "CDS",
			_ => throw new ArgumentOutOfRangeException(nameof(value),
				value, "Unsupported m.Stock exchange type."),
		};

	public static SecurityId ToSecurityId(
		this MStockInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol
				.IsEmpty(instrument.Symbol)
				.IsEmpty(instrument.Token),
			BoardCode = instrument.Exchange,
			Native = $"{instrument.Exchange}/{instrument.Token}",
		};

	public static MStockInstrumentRef ToReference(
		this MStockInstrument instrument)
		=> new(
			instrument.Exchange,
			instrument.Token,
			instrument.TradingSymbol
				.IsEmpty(instrument.Symbol)
				.IsEmpty(instrument.Token),
			instrument.Symbol
				.IsEmpty(instrument.TradingSymbol)
				.IsEmpty(instrument.Token),
			instrument.Lot);

	public static SecurityTypes ToSecurityType(
		this MStockInstrument instrument)
	{
		var value = instrument.InstrumentType?.Trim()
			.ToUpperInvariant();
		if (value?.Contains("OPT",
			StringComparison.Ordinal) == true)
			return SecurityTypes.Option;
		if (value?.Contains("FUT",
			StringComparison.Ordinal) == true)
			return SecurityTypes.Future;
		if (value?.Contains("INDEX",
			StringComparison.Ordinal) == true)
			return SecurityTypes.Index;
		if (value is "MF")
			return SecurityTypes.Fund;
		if (value is "TB" or "SG" or "GB" or "NB")
			return SecurityTypes.Bond;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(
		this MStockInstrument instrument)
	{
		var value = instrument.TradingSymbol
			.IsEmpty(instrument.Symbol)?.ToUpperInvariant();
		if (value?.EndsWith("CE",
			StringComparison.Ordinal) == true)
			return OptionTypes.Call;
		if (value?.EndsWith("PE",
			StringComparison.Ordinal) == true)
			return OptionTypes.Put;
		return null;
	}

	public static DateTime? ToExpiry(this MStockInstrument instrument)
	{
		if (instrument.Expiry.IsEmpty())
			return null;
		var formats = new[]
		{
			"ddMMMyyyy",
			"dd-MMM-yyyy",
			"yyyy-MMM-dd",
			"yyyy-MM-dd",
		};
		return DateTime.TryParseExact(instrument.Expiry.Trim(),
			formats, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var value)
				? DateTime.SpecifyKind(value, DateTimeKind.Utc)
				: null;
	}

	public static bool TryParseMStockNative(this object value,
		out string exchange, out string token)
	{
		exchange = null;
		token = null;
		var text = value?.ToString();
		if (text.IsEmpty())
			return false;
		var separator = text.IndexOf('/');
		if (separator <= 0 || separator == text.Length - 1)
			separator = text.IndexOf(':');
		if (separator <= 0 || separator == text.Length - 1)
			return false;
		exchange = text[..separator].Trim().ToUpperInvariant();
		token = text[(separator + 1)..].Trim();
		return !exchange.IsEmpty() && !token.IsEmpty();
	}

	public static decimal? ToMStockDecimal(this string value)
		=> decimal.TryParse(value, NumberStyles.Any,
			CultureInfo.InvariantCulture, out var result)
				? result
				: null;

	public static decimal? ToMStockDecimal(this JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		var text = value.Value<string>();
		return text.IsEmpty() ||
			text.EqualsIgnoreCase("None")
				? null
				: text.ToMStockDecimal();
	}

	public static DateTimeOffset ToMStockTime(this JToken value,
		DateTimeOffset fallback)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return fallback;
		if (value.Type == JTokenType.Date)
		{
			var dateValue = value.Value<DateTime>();
			return dateValue.Kind == DateTimeKind.Unspecified
				? new(dateValue, _indiaOffset)
				: new DateTimeOffset(dateValue);
		}
		if (value.Type == JTokenType.Integer)
			return DateTimeOffset.FromUnixTimeSeconds(
				value.Value<long>());
		var text = value.Value<string>();
		if (text.IsEmpty())
			return fallback;
		text = text.Trim().Replace(": ", ":",
			StringComparison.Ordinal);
		if (DateTimeOffset.TryParse(text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var offset))
		{
			var timeSeparator = Math.Max(
				text.LastIndexOf(' '), text.LastIndexOf('T'));
			var hasOffset = text.EndsWith("Z",
				StringComparison.OrdinalIgnoreCase) ||
				text.IndexOf('+', timeSeparator + 1) >= 0 ||
				text.IndexOf('-', timeSeparator + 1) >= 0;
			return hasOffset
				? offset
				: new(offset.DateTime, _indiaOffset);
		}
		var formats = new[]
		{
			"yyyy-MMM-dd HH:mm:ss",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd HH:mm",
			"dd-MMM-yyyy HH:mm:ss",
			"HH:mm:ss",
		};
		if (DateTime.TryParseExact(text, formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var date))
		{
			if (text.Length == 8)
				date = DateTime.Today.Add(date.TimeOfDay);
			return new(DateTime.SpecifyKind(date,
				DateTimeKind.Unspecified), _indiaOffset);
		}
		return fallback;
	}

	public static JToken UnwrapMStockData(this JToken value)
		=> value is JObject obj &&
			obj.GetValue("data",
				StringComparison.OrdinalIgnoreCase) is JToken data
				? data
				: value;

	public static JObject[] ToMStockObjects(this JToken value)
	{
		value = value.UnwrapMStockData();
		if (value is JArray array)
			return array.OfType<JObject>().ToArray();
		if (value is JObject obj)
		{
			foreach (var name in new[]
				{
					"fetched", "orders", "trades", "holdings",
					"positions", "orderData",
				})
			{
				var nested = obj.GetValue(name,
					StringComparison.OrdinalIgnoreCase);
				if (nested is JArray nestedArray)
					return nestedArray.OfType<JObject>().ToArray();
				if (nested is JObject nestedObject)
					return [nestedObject];
			}
			return [obj];
		}
		return [];
	}

	public static MStockOrder ToMStockOrder(this JObject value)
	{
		var volume = value.Decimal("quantity", "orderQuantity") ?? 0;
		var filled = value.Decimal("filledshares",
			"filledQuantity", "fillsize") ?? 0;
		return new()
		{
			OrderId = value.String("orderid", "orderId", "order_id"),
			ExchangeOrderId = value.String(
				"exchangeorderid", "exchangeOrderId"),
			Exchange = value.String("exchange"),
			Token = value.String("symboltoken", "symbolToken"),
			Symbol = value.String(
				"tradingsymbol", "tradingSymbol", "symbolname"),
			Side = value.String(
				"transactiontype", "transactionType")
				.EqualsIgnoreCase("SELL")
					? Sides.Sell
					: Sides.Buy,
			OrderType = value.String("ordertype", "orderType"),
			Product = value.String("producttype", "productType"),
			Variety = value.String("variety"),
			Duration = value.String("duration"),
			Price = value.Decimal("price") ?? 0,
			TriggerPrice = value.Decimal(
				"triggerprice", "triggerPrice") ?? 0,
			Volume = volume,
			FilledVolume = filled,
			Balance = value.Decimal("unfilledshares",
				"pendingQuantity", "remainingQuantity") ??
					Math.Max(0, volume - filled),
			AveragePrice = value.Decimal(
				"averageprice", "averagePrice") ?? 0,
			Status = value.String("orderstatus",
				"orderStatus", "status"),
			Text = value.String("text", "message",
				"rejectionReason"),
			Tag = value.String("ordertag", "orderTag"),
			Time = value.Get("exchorderupdatetime", "exchtime",
				"updatetime", "exchangeTimestamp", "timestamp")
				.ToMStockTime(DateTimeOffset.UtcNow),
		};
	}

	public static MStockTrade ToMStockTrade(this JObject value)
		=> new()
		{
			Id = value.String("fillid", "tradeid", "tradeId",
				"uniqueorderid", "uniqueOrderId"),
			OrderId = value.String("orderid", "orderId"),
			Exchange = value.String("exchange"),
			Token = value.String("symboltoken", "symbolToken"),
			Symbol = value.String(
				"tradingsymbol", "tradingSymbol", "symbolname"),
			Side = value.String(
				"transactiontype", "transactionType")
				.EqualsIgnoreCase("SELL")
					? Sides.Sell
					: Sides.Buy,
			Price = value.Decimal("fillprice", "tradeprice",
				"tradePrice", "price") ?? 0,
			Volume = value.Decimal("fillsize", "tradequantity",
				"tradeQuantity", "quantity") ?? 0,
			Time = value.Get("filltime", "tradetime", "tradeTime",
				"exchangeTimestamp", "timestamp")
				.ToMStockTime(DateTimeOffset.UtcNow),
		};

	public static OrderStates ToMStockOrderState(this string value)
	{
		var status = value?
			.Replace(" ", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();
		if (status.IsEmpty())
			return OrderStates.Pending;
		if (status.Contains("REJECT", StringComparison.Ordinal) ||
			status.Contains("FAIL", StringComparison.Ordinal))
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
			status.Contains("TRANSIT", StringComparison.Ordinal) ||
			status.Contains("TRIGGER", StringComparison.Ordinal) ||
			status.Contains("MODIFIED", StringComparison.Ordinal))
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static OrderTypes ToMStockOrderType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"MARKET" => OrderTypes.Market,
			"STOPLOSS_LIMIT" or "STOP_LOSS" or
				"STOPLOSS_MARKET" or "STOP_LOSS_MARKET" =>
					OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToMStockTimeInForce(this string value)
		=> value.EqualsIgnoreCase("IOC")
			? TimeInForce.CancelBalance
			: TimeInForce.PutInQueue;

	public static MStockFeed[] ParseMarketData(byte[] data)
	{
		if (data is null || data.Length == 0)
			return [];
		if (data.Length is 51 or 123 or 379)
			return [ParsePacket(data)];
		if (data.Length < 4)
			throw new InvalidDataException(
				"m.Stock market-data message is truncated.");

		var count = BinaryPrimitives.ReadUInt16BigEndian(
			data.AsSpan(0, 2));
		var offset = 2;
		var result = new List<MStockFeed>(count);
		for (var index = 0; index < count; index++)
		{
			if (offset + 2 > data.Length)
				throw new InvalidDataException(
					"m.Stock packet header is truncated.");
			var length = BinaryPrimitives.ReadUInt16BigEndian(
				data.AsSpan(offset, 2));
			offset += 2;
			if (length is not (51 or 123 or 379) ||
				offset + length > data.Length)
				throw new InvalidDataException(
					$"Invalid m.Stock packet length {length}.");
			result.Add(ParsePacket(
				data[offset..(offset + length)]));
			offset += length;
		}
		return [.. result];
	}

	private static MStockFeed ParsePacket(byte[] data)
	{
		if (data.Length is not (51 or 123 or 379))
			throw new InvalidDataException(
				$"Unsupported m.Stock packet length {data.Length}.");
		var mode = data[0];
		var exchange = ((int)data[1]).ToMStockExchange();
		var token = Encoding.UTF8.GetString(data, 2, 25)
			.TrimEnd('\0', ' ');
		if (token.IsEmpty() ||
			!token.All(char.IsAsciiDigit))
			token = BinaryPrimitives.ReadUInt32LittleEndian(
				data.AsSpan(2, 4)).ToString(
					CultureInfo.InvariantCulture);
		decimal Price(int offset)
			=> BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(offset, 8)) / 100m;
		decimal Quantity(int offset)
			=> BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(offset, 8));
		var exchangeSeconds =
			BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(35, 8));
		var result = new MStockFeed
		{
			Mode = mode,
			Exchange = exchange,
			Token = token,
			Sequence = unchecked((long)
				BinaryPrimitives.ReadUInt64LittleEndian(
					data.AsSpan(27, 8))),
			Time = ToStreamTime(exchangeSeconds),
			LastPrice = Price(43),
			Bids = [],
			Asks = [],
		};
		if (data.Length == 51)
			return result;

		result = new()
		{
			Mode = mode,
			Exchange = exchange,
			Token = token,
			Sequence = result.Sequence,
			Time = result.Time,
			LastPrice = result.LastPrice,
			LastVolume = Quantity(51),
			AveragePrice = Price(59),
			Volume = Quantity(67),
			TotalBidVolume = ToFiniteDecimal(
				BinaryPrimitives.ReadDoubleLittleEndian(
					data.AsSpan(75, 8))),
			TotalAskVolume = ToFiniteDecimal(
				BinaryPrimitives.ReadDoubleLittleEndian(
					data.AsSpan(83, 8))),
			Open = Price(91),
			High = Price(99),
			Low = Price(107),
			Close = Price(115),
			Bids = [],
			Asks = [],
		};
		if (data.Length == 123)
			return result;

		var bids = new List<MStockDepthLevel>(5);
		var asks = new List<MStockDepthLevel>(5);
		for (var index = 0; index < 10; index++)
		{
			var offset = 147 + index * 20;
			var volume = Quantity(offset + 2);
			var price = Price(offset + 10);
			var orders = BinaryPrimitives.ReadUInt16LittleEndian(
				data.AsSpan(offset + 18, 2));
			if (price <= 0 || volume <= 0)
				continue;
			(index < 5 ? bids : asks).Add(
				new(price, volume, orders));
		}
		var tradeSeconds =
			BinaryPrimitives.ReadUInt64LittleEndian(
				data.AsSpan(123, 8));
		return new()
		{
			Mode = result.Mode,
			Exchange = result.Exchange,
			Token = result.Token,
			Sequence = result.Sequence,
			Time = result.Time,
			LastTradeTime = ToStreamTime(tradeSeconds),
			LastPrice = result.LastPrice,
			LastVolume = result.LastVolume,
			AveragePrice = result.AveragePrice,
			Volume = result.Volume,
			TotalBidVolume = result.TotalBidVolume,
			TotalAskVolume = result.TotalAskVolume,
			Open = result.Open,
			High = result.High,
			Low = result.Low,
			Close = result.Close,
			OpenInterest = Quantity(131),
			OpenInterestChange = ToFiniteDecimal(
				BinaryPrimitives.ReadDoubleLittleEndian(
					data.AsSpan(139, 8))),
			UpperLimit = Price(347),
			LowerLimit = Price(355),
			YearHigh = Price(363),
			YearLow = Price(371),
			Bids = [.. bids],
			Asks = [.. asks],
		};
	}

	private static DateTimeOffset ToStreamTime(ulong seconds)
	{
		if (seconds == 0)
			return DateTimeOffset.UtcNow;
		try
		{
			return _streamEpoch.AddSeconds(seconds);
		}
		catch (ArgumentOutOfRangeException)
		{
			return DateTimeOffset.UtcNow;
		}
	}

	private static decimal ToFiniteDecimal(double value)
		=> double.IsFinite(value) &&
			value <= (double)decimal.MaxValue &&
			value >= (double)decimal.MinValue
				? (decimal)value
				: 0;

	public static JToken Get(this JObject value,
		params string[] names)
	{
		if (value is null)
			return null;
		foreach (var name in names)
		{
			var token = value.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is not null &&
				token.Type is not JTokenType.Null and
					not JTokenType.Undefined &&
				(token.Type != JTokenType.String ||
					!token.Value<string>().IsEmpty() &&
					!token.Value<string>().EqualsIgnoreCase("None")))
				return token;
		}
		return null;
	}

	public static string String(this JObject value,
		params string[] names)
	{
		var token = value.Get(names);
		return token?.Value<string>();
	}

	public static decimal? Decimal(this JObject value,
		params string[] names)
	{
		foreach (var name in names)
		{
			var result = value.Get(name).ToMStockDecimal();
			if (result is not null)
				return result;
		}
		return null;
	}
}
