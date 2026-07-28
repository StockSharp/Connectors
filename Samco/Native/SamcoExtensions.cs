namespace StockSharp.Samco.Native;

static class SamcoExtensions
{
	private static readonly TimeSpan _indiaOffset =
		TimeSpan.FromMinutes(330);
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "1",
			[TimeSpan.FromMinutes(3)] = "3",
			[TimeSpan.FromMinutes(5)] = "5",
			[TimeSpan.FromMinutes(10)] = "10",
			[TimeSpan.FromMinutes(15)] = "15",
			[TimeSpan.FromMinutes(30)] = "30",
			[TimeSpan.FromHours(1)] = "60",
			[TimeSpan.FromDays(1)] = "D",
		};

	public static IEnumerable<TimeSpan> TimeFrames =>
		_timeFrames.Keys;

	public static string ToSamcoInterval(this TimeSpan value)
		=> _timeFrames.TryGetValue(value, out var interval)
			? interval
			: throw new NotSupportedException(
				$"Samco candle interval '{value}' is unsupported.");

	public static SamcoInstrument[] ParseInstruments(string text)
	{
		if (text.IsEmpty())
			return [];
		var lines = text.Split(
			['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length < 2)
			return [];
		var headers = ParseCsvLine(lines[0])
			.Select(NormalizeHeader)
			.ToArray();
		var result = new List<SamcoInstrument>(lines.Length - 1);
		foreach (var line in lines.Skip(1))
		{
			var values = ParseCsvLine(line);
			string Get(string name)
			{
				var index = Array.IndexOf(headers,
					NormalizeHeader(name));
				return index >= 0 && index < values.Length
					? values[index]?.Trim()
					: null;
			}
			var instrument = new SamcoInstrument
			{
				Exchange = Get("exchange"),
				ExchangeSegment = Get("exchangeSegment"),
				SymbolCode = Get("symbolCode"),
				TradingSymbol = Get("tradingSymbol"),
				Name = Get("name"),
				LastPrice = Get("lastPrice"),
				Instrument = Get("instrument"),
				LotSize = Get("lotSize"),
				StrikePrice = Get("strikePrice"),
				ExpiryDate = Get("expiryDate"),
				TickSize = Get("tickSize"),
			};
			if (!instrument.Exchange.IsEmpty() &&
				!instrument.SymbolCode.IsEmpty())
				result.Add(instrument);
		}
		return [.. result];
	}

	private static string[] ParseCsvLine(string line)
	{
		var result = new List<string>();
		var value = new StringBuilder();
		var quoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var character = line[index];
			if (character == '"')
			{
				if (quoted && index + 1 < line.Length &&
					line[index + 1] == '"')
				{
					value.Append('"');
					index++;
				}
				else
					quoted = !quoted;
			}
			else if (character == ',' && !quoted)
			{
				result.Add(value.ToString());
				value.Clear();
			}
			else
				value.Append(character);
		}
		result.Add(value.ToString());
		return [.. result];
	}

	private static string NormalizeHeader(string value)
		=> new(value.Where(char.IsLetterOrDigit)
			.Select(char.ToLowerInvariant).ToArray());

	public static SamcoInstrumentRef ToReference(
		this SamcoInstrument instrument)
		=> new(
			instrument.Exchange?.Trim().ToUpperInvariant(),
			instrument.SymbolCode,
			instrument.TradingSymbol
				.IsEmpty(instrument.Name)
				.IsEmpty(instrument.SymbolCode),
			instrument.Name
				.IsEmpty(instrument.TradingSymbol)
				.IsEmpty(instrument.SymbolCode),
			instrument.Lot,
			instrument.Instrument);

	public static SecurityId ToSecurityId(
		this SamcoInstrument instrument)
		=> ToSecurityId(instrument.ToReference());

	public static SecurityId ToSecurityId(
		this SamcoInstrumentRef instrument)
		=> new()
		{
			SecurityCode = instrument.TradingSymbol,
			BoardCode = instrument.Exchange,
			Native = instrument.SymbolCode,
		};

	public static SecurityTypes ToSecurityType(
		this SamcoInstrument instrument)
	{
		var value = instrument.Instrument?.Trim()
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
		if (value is "TB" or "GB" or "SG" or "BOND")
			return SecurityTypes.Bond;
		return SecurityTypes.Stock;
	}

	public static OptionTypes? ToOptionType(
		this SamcoInstrument instrument)
	{
		var value = instrument.TradingSymbol?.Trim()
			.ToUpperInvariant();
		if (value?.EndsWith("CE",
			StringComparison.Ordinal) == true)
			return OptionTypes.Call;
		if (value?.EndsWith("PE",
			StringComparison.Ordinal) == true)
			return OptionTypes.Put;
		return null;
	}

	public static DateTime? ToExpiry(this SamcoInstrument instrument)
	{
		if (instrument.ExpiryDate.IsEmpty())
			return null;
		var formats = new[]
		{
			"yyyy-MM-dd",
			"dd-MM-yyyy",
			"dd-MMM-yyyy",
			"ddMMMyyyy",
			"ddMMMyy",
		};
		return DateTime.TryParseExact(instrument.ExpiryDate.Trim(),
			formats, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var value)
				? DateTime.SpecifyKind(value, DateTimeKind.Utc)
				: null;
	}

	public static decimal? ToSamcoDecimal(this string value)
	{
		if (value.IsEmpty())
			return null;
		value = value.Trim().Replace(",",
			string.Empty, StringComparison.Ordinal);
		return decimal.TryParse(value, NumberStyles.Any,
			CultureInfo.InvariantCulture, out var result)
				? result
				: null;
	}

	public static decimal? ToSamcoDecimal(this JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return value.Value<string>().ToSamcoDecimal();
	}

	public static DateTimeOffset ToSamcoTime(this JToken value,
		DateTimeOffset fallback)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return fallback;
		if (value.Type == JTokenType.Integer)
		{
			var number = value.Value<long>();
			try
			{
				return number > 10_000_000_000
					? DateTimeOffset.FromUnixTimeMilliseconds(number)
					: DateTimeOffset.FromUnixTimeSeconds(number);
			}
			catch (ArgumentOutOfRangeException)
			{
				return fallback;
			}
		}
		var text = value.Value<string>();
		if (text.IsEmpty())
			return fallback;
		var formats = new[]
		{
			"dd-MMM-yyyy HH:mm:ss",
			"dd/MM/yyyy HH:mm:ss",
			"dd/MM/yy HH:mm:ss",
			"yyyy-MM-dd HH:mm:ss.F",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd",
			"ddMMMyyyy hh:mm:ss tt",
		};
		if (DateTime.TryParseExact(text.Trim(), formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var date))
			return new(DateTime.SpecifyKind(date,
				DateTimeKind.Unspecified), _indiaOffset);
		if (DateTimeOffset.TryParse(text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var offset))
			return offset;
		return fallback;
	}

	public static JObject[] ToSamcoObjects(this JToken value,
		params string[] names)
	{
		if (value is JArray array)
			return array.OfType<JObject>().ToArray();
		if (value is not JObject obj)
			return [];
		foreach (var name in names)
		{
			var nested = obj.Get(name);
			if (nested is JArray nestedArray)
				return nestedArray.OfType<JObject>().ToArray();
			if (nested is JObject nestedObject)
				return [nestedObject];
		}
		return [obj];
	}

	public static SamcoOrder ToSamcoOrder(this JObject value)
	{
		var volume = value.Decimal("quantity", "totalQuantity") ?? 0;
		var filled = value.Decimal("filledQuantity") ?? 0;
		return new()
		{
			OrderId = value.String("orderNumber", "ordernumber"),
			ExchangeOrderId = value.String("exchangeOrderId",
				"exchangeOrderNumber"),
			Exchange = value.String("exchange"),
			SymbolCode = value.String("symbol", "symbolCode",
				"listingId"),
			Symbol = value.String("tradingSymbol", "symbolName"),
			Side = value.String("transactionType") is "S" or "SELL"
				? Sides.Sell
				: Sides.Buy,
			OrderType = value.String("orderType"),
			Product = value.String("productCode", "productType"),
			Validity = value.String("orderValidity"),
			Price = value.Decimal("orderPrice", "price") ?? 0,
			TriggerPrice = value.Decimal("triggerPrice") ?? 0,
			Volume = volume,
			FilledVolume = filled,
			Balance = value.Decimal("unfilledQuantity",
				"pendingQuantity") ?? Math.Max(0, volume - filled),
			AveragePrice = value.Decimal("averagePrice",
				"avgExecutionPrice", "fillPrice") ?? 0,
			Status = value.String("orderStatus",
				"exchangeOrderStatus", "status"),
			Text = value.String("rejectionReason",
				"statusMessage"),
			Time = value.Get("orderTime", "serverTime")
				.ToSamcoTime(DateTimeOffset.UtcNow),
		};
	}

	public static SamcoTrade ToSamcoTrade(this JObject value)
	{
		var date = value.String("tradeDate");
		var time = value.String("tradeTime");
		return new()
		{
			Id = value.String("tradeNumber"),
			OrderId = value.String("orderNumber"),
			Exchange = value.String("exchange"),
			SymbolCode = value.String("symbol", "symbolCode",
				"listingId"),
			Symbol = value.String("tradingSymbol", "symbolName"),
			Side = value.String("transactionType") is "S" or "SELL"
				? Sides.Sell
				: Sides.Buy,
			Price = value.Decimal("tradePrice") ?? 0,
			Volume = value.Decimal("filledQuantity",
				"quantity") ?? 0,
			Time = new JValue(
				date.IsEmpty() ? time : $"{date} {time}")
					.ToSamcoTime(value.Get("orderTime")
						.ToSamcoTime(DateTimeOffset.UtcNow)),
		};
	}

	public static OrderStates ToSamcoOrderState(this string value)
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
			status.Contains("PARTIAL", StringComparison.Ordinal) ||
			status.Contains("TRIGGER", StringComparison.Ordinal))
			return OrderStates.Active;
		return OrderStates.Pending;
	}

	public static OrderTypes ToSamcoOrderType(this string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"MKT" or "MARKET" => OrderTypes.Market,
			"SL" or "SL-M" => OrderTypes.Conditional,
			_ => OrderTypes.Limit,
		};

	public static TimeInForce ToSamcoTimeInForce(this string value)
		=> value.EqualsIgnoreCase("IOC")
			? TimeInForce.CancelBalance
			: TimeInForce.PutInQueue;

	public static SamcoFeed ParseFeed(string text)
	{
		if (text.IsEmpty())
			return null;
		var token = JToken.Parse(text);
		if (token is JObject wrapper &&
			wrapper.Get("response") is JObject response)
			token = response.Get("data") ?? response;
		if (token is not JObject value)
			return null;
		var symbol = value.String("sym", "symbol", "symbolCode",
			"listingId", "tradingSymbol");
		if (symbol.IsEmpty())
			return null;
		var bids = ParseDepth(value, true);
		var asks = ParseDepth(value, false);
		var time = value.Get("lttUTC", "timestamp", "ltt")
			.ToSamcoTime(DateTimeOffset.UtcNow);
		return new()
		{
			SymbolCode = symbol,
			Time = time,
			LastTradeTime = value.Get("lttUTC", "ltt", "lTrdT")
				.ToSamcoTime(time),
			LastPrice = value.Decimal("ltp",
				"lastTradedPrice") ?? 0,
			LastVolume = value.Decimal("ltq",
				"lastTradedQuantity") ?? 0,
			AveragePrice = value.Decimal("avgPr",
				"averagePrice") ?? 0,
			Volume = value.Decimal("vol", "volume") ?? 0,
			TotalBidVolume = value.Decimal("tBQ",
				"totalBuyQuantity") ?? bids.Sum(
					static level => level.Volume),
			TotalAskVolume = value.Decimal("tSQ",
				"totalSellQuantity") ?? asks.Sum(
					static level => level.Volume),
			Open = value.Decimal("o", "open") ?? 0,
			High = value.Decimal("h", "high") ?? 0,
			Low = value.Decimal("l", "low") ?? 0,
			Close = value.Decimal("c", "close") ?? 0,
			OpenInterest = value.Decimal("oI",
				"openInterest") ?? 0,
			OpenInterestChange = value.Decimal("oIChg",
				"openInterestChange") ?? 0,
			UpperLimit = value.Decimal("upperCircuitLimit") ?? 0,
			LowerLimit = value.Decimal("lowerCircuitLimit") ?? 0,
			YearHigh = value.Decimal("yH",
				"yearlyHighPrice") ?? 0,
			YearLow = value.Decimal("yL",
				"yearlyLowPrice") ?? 0,
			Bids = bids,
			Asks = asks,
		};
	}

	public static SamcoFeed ToSamcoFeed(this JObject value)
	{
		var feed = ParseFeed(value.ToString(Formatting.None));
		if (feed is null)
			return null;
		var bids = ParseDepth(value, true);
		var asks = ParseDepth(value, false);
		return new()
		{
			SymbolCode = feed.SymbolCode,
			Time = feed.Time,
			LastTradeTime = feed.LastTradeTime,
			LastPrice = feed.LastPrice,
			LastVolume = feed.LastVolume,
			AveragePrice = feed.AveragePrice,
			Volume = feed.Volume,
			TotalBidVolume = feed.TotalBidVolume,
			TotalAskVolume = feed.TotalAskVolume,
			Open = feed.Open,
			High = feed.High,
			Low = feed.Low,
			Close = feed.Close,
			OpenInterest = feed.OpenInterest,
			OpenInterestChange = feed.OpenInterestChange,
			UpperLimit = feed.UpperLimit,
			LowerLimit = feed.LowerLimit,
			YearHigh = feed.YearHigh,
			YearLow = feed.YearLow,
			Bids = bids,
			Asks = asks,
		};
	}

	private static SamcoDepthLevel[] ParseDepth(JObject value,
		bool bids)
	{
		var names = bids
			? new[] { "bestBids", "bids", "buy" }
			: new[] { "bestAsks", "asks", "sell" };
		JArray array = null;
		foreach (var name in names)
			if (value.Get(name) is JArray found)
			{
				array = found;
				break;
			}
		if (array is not null)
			return array.OfType<JObject>()
				.Select(static level => new SamcoDepthLevel(
					level.Decimal("price", "p") ?? 0,
					level.Decimal("quantity", "size", "qty", "q") ??
						0,
					(int)(level.Decimal("number", "orders",
						"count") ?? 0)))
				.Where(static level =>
					level.Price > 0 && level.Volume > 0)
				.Take(5)
				.ToArray();

		var result = new List<SamcoDepthLevel>(5);
		var prefix = bids ? "b" : "a";
		for (var index = 1; index <= 5; index++)
		{
			var price = value.Decimal(
				$"{prefix}{index}p", $"{prefix}Pr{index}",
				index == 1 ? $"{prefix}Pr" : string.Empty) ?? 0;
			var volume = value.Decimal(
				$"{prefix}{index}q", $"{prefix}Sz{index}",
				index == 1 ? $"{prefix}Sz" : string.Empty) ?? 0;
			var orders = (int)(value.Decimal(
				$"{prefix}{index}n", $"{prefix}Ord{index}") ?? 0);
			if (price > 0 && volume > 0)
				result.Add(new(price, volume, orders));
		}
		return [.. result];
	}

	public static JToken Get(this JObject value,
		params string[] names)
	{
		if (value is null)
			return null;
		foreach (var name in names)
		{
			if (name.IsEmpty())
				continue;
			var token = value.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (token is not null &&
				token.Type is not JTokenType.Null and
					not JTokenType.Undefined &&
				(token.Type != JTokenType.String ||
					!token.Value<string>().IsEmpty()))
				return token;
		}
		return null;
	}

	public static string String(this JObject value,
		params string[] names)
		=> value.Get(names)?.Value<string>();

	public static decimal? Decimal(this JObject value,
		params string[] names)
		=> value.Get(names).ToSamcoDecimal();
}
