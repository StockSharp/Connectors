namespace StockSharp.SSI.Native;

static class SSIExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames =
		new()
		{
			[TimeSpan.FromMinutes(1)] = "1m",
			[TimeSpan.FromMinutes(3)] = "3m",
			[TimeSpan.FromMinutes(5)] = "5m",
			[TimeSpan.FromMinutes(15)] = "15m",
			[TimeSpan.FromHours(1)] = "1h",
			[TimeSpan.FromDays(1)] = "1d",
			[TimeSpan.FromDays(7)] = "1w",
		};

	private static readonly TimeSpan _vietnamOffset =
		TimeSpan.FromHours(7);

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames.Keys;

	public static string ToSSIInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var interval)
			? interval
			: throw new NotSupportedException(
				$"SSI candle interval '{timeFrame}' is unsupported.");

	public static JObject UnwrapSSIData(this JToken source)
	{
		if (source is not JObject obj)
			return null;
		return obj.Get("data") as JObject ?? obj;
	}

	public static JObject[] ToSSIObjects(this JToken source,
		params string[] names)
	{
		if (source is JArray array)
			return array.OfType<JObject>().ToArray();
		if (source is not JObject obj)
			return [];
		var candidates = names.Concat(new[]
		{
			"data", "items", "orderList", "orders",
		}).Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var name in candidates)
		{
			var nested = obj.Get(name);
			if (nested is JArray nestedArray)
				return nestedArray.OfType<JObject>().ToArray();
			if (nested is JObject nestedObject)
			{
				var result = nestedObject.ToSSIObjects(names);
				if (result.Length > 0)
					return result;
			}
		}
		return [];
	}

	public static SSIInstrument ToSSIInstrument(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		return new()
		{
			Symbol = source.String("symbol"),
			Board = source.String("board"),
			Name = source.String("symbolNameEn", "symbolNameVi",
				"name"),
			LotSize = source.Int("lotSize") ?? 0,
			MaturityDate = source.Get("maturityDate").ToSSITime(),
			FirstTradingDate = source.Get("firstTradingDate")
				.ToSSITime(),
			LastTradingDate = source.Get("lastTradingDate").ToSSITime(),
			UnderlyingSymbol = source.String("cwUnderlyingSymbol"),
			ExercisePrice = source.Decimal("cwExercisePrice"),
			ExecutionRatio = source.Decimal("cwExecutionRatio"),
		};
	}

	public static SSICandle[] ToSSICandles(this JToken source)
		=> source.ToSSIObjects("candles", "ohlc")
			.Select(static value => value.ToSSICandle())
			.Where(static value => !value.Symbol.IsEmpty() &&
				value.Time != default)
			.OrderBy(static value => value.Time)
			.ToArray();

	public static SSICandle ToSSICandle(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		return new()
		{
			Symbol = source.String("symbol", "s"),
			Time = source.Get("tradingDate", "st", "time", "t")
				.ToSSITime() ?? default,
			Open = source.Decimal("open", "o") ?? 0,
			High = source.Decimal("high", "h") ?? 0,
			Low = source.Decimal("low", "l") ?? 0,
			Close = source.Decimal("close", "c", "price", "p") ?? 0,
			Volume = source.Decimal("volume", "v", "quantity", "q") ??
				0,
			Turnover = source.Decimal("value", "turnover") ?? 0,
		};
	}

	public static SSITrade ToSSITrade(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		var side = source.String("si", "side");
		return new()
		{
			Symbol = source.String("s", "symbol"),
			Time = source.Get("t", "tradingTime", "time").ToSSITime() ??
				DateTimeOffset.UtcNow,
			Price = source.Decimal("p", "price", "matchedPrice") ?? 0,
			Volume = source.Decimal("q", "quantity", "matchedQty") ?? 0,
			Side = side.EqualsIgnoreCase("B") ||
				side.EqualsIgnoreCase("Buy")
					? Sides.Buy
					: side.EqualsIgnoreCase("S") ||
						side.EqualsIgnoreCase("Sell")
						? Sides.Sell
						: null,
			TotalVolume = source.Decimal("v", "totalVolume") ?? 0,
		};
	}

	public static SSIDepth ToSSIDepth(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		return new()
		{
			Symbol = source.String("s", "symbol"),
			Time = source.Get("t", "tradingTime", "time").ToSSITime() ??
				DateTimeOffset.UtcNow,
			Bids = ParseLevels(source.Get("bids")),
			Asks = ParseLevels(source.Get("asks")),
		};
	}

	private static SSILevel[] ParseLevels(JToken source)
	{
		if (source is not JArray values)
			return [];
		return values.Select(static value =>
			{
				if (value is JArray pair && pair.Count >= 2)
					return new SSILevel(
						pair[0].ToSSIDecimal() ?? 0,
						pair[1].ToSSIDecimal() ?? 0);
				if (value is JObject obj)
					return new SSILevel(
						obj.Decimal("price", "p") ?? 0,
						obj.Decimal("volume", "quantity", "q") ?? 0);
				return default;
			})
			.Where(static value => value.Price > 0 && value.Volume > 0)
			.ToArray();
	}

	public static SSIOrder ToSSIOrder(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		var volume = source.Decimal("quantity", "orderQuantity") ?? 0;
		var filled = source.Decimal("filledQty", "filledQuantity",
			"matchedQuantity") ?? 0;
		var cancelled = source.Decimal("cancelQty", "cancelQuantity") ??
			0;
		var balance = source.Decimal("osQty", "osQuantity");
		return new()
		{
			Account = source.String("accountNo", "account"),
			ClientRequestId = source.String("clientRequestId"),
			OrderId = source.String("orderId"),
			Symbol = source.String("symbol", "instrumentID"),
			Side = source.String("side").EqualsIgnoreCase("S")
				? Sides.Sell
				: Sides.Buy,
			OrderType = source.String("orderType"),
			Price = source.Decimal("price") ?? 0,
			AveragePrice = source.Decimal("avgPrice",
				"averagePrice") ?? 0,
			Volume = volume,
			FilledVolume = filled,
			CancelledVolume = cancelled,
			Balance = balance ?? Math.Max(0,
				volume - filled - cancelled),
			Status = source.String("orderStatus", "status",
				"processStatus"),
			Time = source.Get("modifyTime", "modifiedTime",
				"inputTime", "updatedTime", "time").ToSSITime() ??
				DateTimeOffset.UtcNow,
			Message = source.String("rejectReason", "message"),
		};
	}

	public static SSIOrderMatch ToSSIOrderMatch(this JObject source)
	{
		source = source.UnwrapSSIData() ?? source;
		return new()
		{
			Id = source.String("notifyId", "matchId", "tradeId"),
			Account = source.String("accountNo"),
			OrderId = source.String("orderId"),
			Symbol = source.String("symbol"),
			Side = source.String("side").EqualsIgnoreCase("S")
				? Sides.Sell
				: Sides.Buy,
			Price = source.Decimal("matchedPrice", "price") ?? 0,
			Volume = source.Decimal("matchedQty", "quantity") ?? 0,
			Time = source.Get("matchedTime", "tradingTime",
				"tradingDate", "time").ToSSITime() ??
				DateTimeOffset.UtcNow,
		};
	}

	public static OrderStates ToSSIOrderState(this string status)
		=> status?.Trim().ToUpperInvariant() switch
		{
			"FF" or "FFPC" or "CL" or "EX" or "FILLED" or
				"CANCELED" or "CANCELLED" or "EXPIRED" =>
				OrderStates.Done,
			"RJ" or "REJECTED" or "FAILED" => OrderStates.Failed,
			"PD" or "WA" or "RS" or "SD" or "QU" or "PF" or
				"WM" or "WC" or "IAV" or "NEW" or
				"PARTIAL_FILLED" or "PENDING" =>
				OrderStates.Active,
			_ => OrderStates.Pending,
		};

	public static string ToSSIBoard(this string board)
		=> board?.Trim().ToUpperInvariant() switch
		{
			"HNX" => BoardCodes.Hnx,
			"UPCOM" => BoardCodes.Upcom,
			_ => BoardCodes.Hose,
		};

	public static SecurityTypes ToSSISecurityType(
		this SSIInstrument instrument)
	{
		if (!instrument.UnderlyingSymbol.IsEmpty())
			return SecurityTypes.Option;
		if (instrument.Symbol?.StartsWith("VN30F",
			StringComparison.OrdinalIgnoreCase) == true)
			return SecurityTypes.Future;
		return SecurityTypes.Stock;
	}

	private static JToken Get(this JObject source, params string[] names)
	{
		if (source is null)
			return null;
		foreach (var name in names)
		{
			var value = source.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (value is not null &&
				value.Type is not JTokenType.Null and
					not JTokenType.Undefined)
				return value;
		}
		return null;
	}

	private static string String(this JObject source,
		params string[] names)
		=> source.Get(names)?.Value<string>();

	private static int? Int(this JObject source, params string[] names)
		=> source.Get(names)?.Value<int?>();

	private static decimal? Decimal(this JObject source,
		params string[] names)
		=> source.Get(names).ToSSIDecimal();

	private static decimal? ToSSIDecimal(this JToken source)
	{
		if (source is null)
			return null;
		if (source.Type is JTokenType.Integer or JTokenType.Float)
			return source.Value<decimal>();
		return decimal.TryParse(source.Value<string>(),
			NumberStyles.Any, CultureInfo.InvariantCulture,
			out var value)
				? value
				: null;
	}

	public static DateTimeOffset? ToSSITime(this JToken source)
	{
		if (source is null)
			return null;
		if (source.Type == JTokenType.Date)
		{
			if (source is JValue
				{
					Value: DateTimeOffset storedOffset
				})
				return storedOffset.ToOffset(_vietnamOffset);
			var date = source.Value<DateTime>();
			return date.Kind == DateTimeKind.Unspecified
				? new(date, _vietnamOffset)
				: new DateTimeOffset(date).ToOffset(_vietnamOffset);
		}
		if (source.Type is JTokenType.Integer)
		{
			var timestamp = source.Value<long>();
			return timestamp > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
				: DateTimeOffset.FromUnixTimeSeconds(timestamp);
		}
		var text = source.Value<string>();
		if (text.IsEmpty())
			return null;
		if (long.TryParse(text, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numeric))
			return numeric > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
				: DateTimeOffset.FromUnixTimeSeconds(numeric);
		var hasOffset =
			text.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
			text.Length >= 6 &&
			(text[^6] is '+' or '-') && text[^3] == ':' ||
			text.Length >= 5 &&
			(text[^5] is '+' or '-') &&
			char.IsDigit(text[^4]) && char.IsDigit(text[^1]);
		if (hasOffset &&
			DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
				DateTimeStyles.AllowWhiteSpaces, out var offset))
			return offset;
		if (!DateTime.TryParse(text, CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var local))
			return null;
		if (local.Kind != DateTimeKind.Unspecified)
			local = DateTime.SpecifyKind(local,
				DateTimeKind.Unspecified);
		return new(local, _vietnamOffset);
	}
}
