namespace StockSharp.Settrade.Native;

static class SettradeExtensions
{
	private static readonly Dictionary<TimeSpan, string> _timeFrames = new()
	{
		[TimeSpan.FromMinutes(1)] = "1m",
		[TimeSpan.FromMinutes(3)] = "3m",
		[TimeSpan.FromMinutes(5)] = "5m",
		[TimeSpan.FromMinutes(10)] = "10m",
		[TimeSpan.FromMinutes(15)] = "15m",
		[TimeSpan.FromMinutes(30)] = "30m",
		[TimeSpan.FromHours(1)] = "60m",
		[TimeSpan.FromHours(2)] = "120m",
		[TimeSpan.FromHours(4)] = "240m",
		[TimeSpan.FromDays(1)] = "1d",
		[TimeSpan.FromDays(7)] = "1w",
	};

	public static IEnumerable<TimeSpan> TimeFrames =>
		_timeFrames.Keys;

	public static string ToSettradeInterval(this TimeSpan timeFrame)
		=> _timeFrames.TryGetValue(timeFrame, out var value)
			? value
			: throw new NotSupportedException(
				$"Settrade candle interval '{timeFrame}' is unsupported.");

	public static SettradeLevel1 ToLevel1(this JObject source)
	{
		source = source.UnwrapObject();
		return new()
		{
			Symbol = source.String("symbol", "seriesId", "securitySymbol"),
			ProjectedOpenPrice = source.Decimal(
				"projectedOpenPrice", "projected_open_price"),
			High = source.Decimal("high", "highPrice"),
			Low = source.Decimal("low", "lowPrice"),
			Last = source.Decimal("last", "lastPrice", "close"),
			Change = source.Decimal("change", "priceChange"),
			TotalVolume = source.Decimal("totalVolume", "volume"),
			TotalValue = source.Decimal("totalValue", "value", "turnover"),
			MarketStatus = source.Int("marketStatus", "market_status") ?? 0,
		};
	}

	public static SettradeOrderBook ToOrderBook(this JObject source)
	{
		source = source.UnwrapObject();
		var bids = ParseLevels(source.Get("bids", "bid", "buyQuotes"));
		var asks = ParseLevels(source.Get("asks", "offers", "ask",
			"sellQuotes"));
		if (bids.Length == 0)
			bids = ParseNumberedLevels(source, "bidPrice", "bidVolume");
		if (asks.Length == 0)
			asks = ParseNumberedLevels(source, "askPrice", "askVolume");
		return new()
		{
			Symbol = source.String("symbol", "seriesId", "securitySymbol"),
			Bids = bids,
			Asks = asks,
		};
	}

	private static SettradeBookLevel[] ParseLevels(JToken token)
	{
		if (token is not JArray array)
			return [];
		return array.Select(item =>
			{
				if (item is JArray values && values.Count >= 2)
					return new SettradeBookLevel(
						values[0].ToDecimal() ?? 0,
						values[1].ToDecimal() ?? 0);
				if (item is JObject obj)
					return new SettradeBookLevel(
						obj.Decimal("price", "bidPrice", "askPrice") ?? 0,
						obj.Decimal("volume", "qty", "quantity",
							"bidVolume", "askVolume") ?? 0);
				return default;
			})
			.Where(static level => level.Price > 0 && level.Volume > 0)
			.ToArray();
	}

	private static SettradeBookLevel[] ParseNumberedLevels(
		JObject source, string pricePrefix, string volumePrefix)
	{
		var result = new List<SettradeBookLevel>();
		for (var index = 1; index <= 10; index++)
		{
			var price = source.Decimal(pricePrefix + index);
			var volume = source.Decimal(volumePrefix + index);
			if (price is > 0 && volume is > 0)
				result.Add(new(price.Value, volume.Value));
		}
		return result.ToArray();
	}

	public static SettradeCandle[] ToCandles(this JToken source)
	{
		if (source is JObject root)
		{
			var nested = root.Get("data", "candlesticks", "items");
			if (nested is not null && !ReferenceEquals(nested, source))
				return nested.ToCandles();
			var times = root.Get("t", "time", "times") as JArray;
			var opens = root.Get("o", "open", "opens") as JArray;
			var highs = root.Get("h", "high", "highs") as JArray;
			var lows = root.Get("l", "low", "lows") as JArray;
			var closes = root.Get("c", "close", "closes") as JArray;
			var volumes = root.Get("v", "volume", "volumes") as JArray;
			var values = root.Get("value", "values", "turnover") as JArray;
			if (times is not null && opens is not null &&
				highs is not null && lows is not null &&
				closes is not null)
			{
				var count = new[]
					{
						times.Count, opens.Count, highs.Count, lows.Count,
						closes.Count,
					}.Min();
				return Enumerable.Range(0, count)
					.Select(index => new SettradeCandle
					{
						Time = times[index].ToDateTime() ??
							DateTime.MinValue,
						Open = opens[index].ToDecimal() ?? 0,
						High = highs[index].ToDecimal() ?? 0,
						Low = lows[index].ToDecimal() ?? 0,
						Close = closes[index].ToDecimal() ?? 0,
						Volume = volumes is not null &&
							index < volumes.Count
								? volumes[index].ToDecimal() ?? 0
								: 0,
						Turnover = values is not null &&
							index < values.Count
								? values[index].ToDecimal() ?? 0
								: 0,
					})
					.Where(static candle => candle.Time !=
						DateTime.MinValue)
					.OrderBy(static candle => candle.Time)
					.ToArray();
			}
		}
		if (source is not JArray array)
			return [];
		return array.OfType<JObject>()
			.Select(item => new SettradeCandle
			{
				Symbol = item.String("symbol", "seriesId"),
				Interval = item.String("interval"),
				Sequence = item.Long("lastSequence", "sequence") ?? 0,
				Time = item.Get("time", "timestamp", "datetime",
					"dateTime", "openTime").ToDateTime() ??
					DateTime.MinValue,
				Open = item.Decimal("open", "openPrice") ?? 0,
				High = item.Decimal("high", "highPrice") ?? 0,
				Low = item.Decimal("low", "lowPrice") ?? 0,
				Close = item.Decimal("close", "closePrice") ?? 0,
				Volume = item.Decimal("volume", "totalVolume") ?? 0,
				Turnover = item.Decimal("value", "turnover",
					"totalValue") ?? 0,
			})
			.Where(static candle => candle.Time != DateTime.MinValue)
			.OrderBy(static candle => candle.Time)
			.ToArray();
	}

	public static SettradeOrder ToSettradeOrder(this JObject source)
	{
		source = source.UnwrapObject();
		var side = source.String("side", "buySell", "longShort");
		return new()
		{
			OrderNo = source.String("orderNo", "order_no", "id"),
			AccountNo = source.String("accountNo", "account_no"),
			Symbol = source.String("symbol", "seriesId", "series_id"),
			Side = side,
			Position = source.String("position"),
			PriceType = source.String("priceType", "price_type"),
			Validity = source.String("validity", "valid", "validityType"),
			Status = source.String("status", "showStatus",
				"showOrderStatus"),
			Price = source.Decimal("price", "orderPrice") ?? 0,
			Volume = source.Decimal("vol", "volume", "qty",
				"orderVolume") ?? 0,
			MatchedVolume = source.Decimal("matched", "matchedVolume",
				"matchQty", "match_qty") ?? 0,
			BalanceVolume = source.Decimal("balance", "balanceVolume",
				"balanceQty") ?? 0,
			CancelledVolume = source.Decimal("cancelled",
				"cancelledVolume", "cancelQty") ?? 0,
			Time = source.Get("entryTime", "tradeTime",
				"transactionTime", "time", "createdAt").ToDateTime() ??
				DateTime.UtcNow,
			Version = source.Int("version") ?? 0,
			CanCancel = source.Bool("canCancel", "can_cancel") ?? false,
		};
	}

	public static OrderStates ToOrderState(this string status)
	{
		status = status?.Trim().ToUpperInvariant();
		return status switch
		{
			"M" or "MATCHED" or "FILLED" or "DONE" => OrderStates.Done,
			"C" or "E" or "CANCELLED" or "CANCELED" or "EXPIRED" =>
				OrderStates.Done,
			"REJECTED" or "R" or "FAILED" => OrderStates.Failed,
			"S" or "SX" or "MP" or "OPEN" or "PENDING" or "ACTIVE" or
				"PARTIALLY_FILLED" => OrderStates.Active,
			_ => OrderStates.Pending,
		};
	}

	public static Sides ToSide(this string side)
		=> side.EqualsIgnoreCase("Sell") ||
			side.EqualsIgnoreCase("Short")
				? Sides.Sell
				: Sides.Buy;

	public static TimeInForce ToTimeInForce(this string validity)
		=> validity?.Trim().ToUpperInvariant() switch
		{
			"FOK" => TimeInForce.MatchOrCancel,
			"IOC" => TimeInForce.CancelBalance,
			_ => TimeInForce.PutInQueue,
		};

	public static string ToSettradeValidity(
		this TimeInForce? timeInForce, DateTime? validTillDate)
		=> validTillDate is not null
			? "Date"
			: timeInForce switch
			{
				TimeInForce.MatchOrCancel => "FOK",
				TimeInForce.CancelBalance => "IOC",
				_ => "Day",
			};

	private static JObject UnwrapObject(this JObject source)
		=> source.Get("data") as JObject ?? source;

	private static JToken Get(this JObject source, params string[] names)
	{
		if (source is null)
			return null;
		foreach (var name in names)
		{
			var value = source.GetValue(name,
				StringComparison.OrdinalIgnoreCase);
			if (value is not null && value.Type is not JTokenType.Null &&
				value.Type is not JTokenType.Undefined)
				return value;
		}
		return null;
	}

	private static string String(this JObject source,
		params string[] names)
		=> source.Get(names)?.Value<string>();

	private static decimal? Decimal(this JObject source,
		params string[] names)
		=> source.Get(names).ToDecimal();

	private static int? Int(this JObject source, params string[] names)
		=> source.Get(names)?.Value<int?>();

	private static long? Long(this JObject source, params string[] names)
		=> source.Get(names)?.Value<long?>();

	private static bool? Bool(this JObject source, params string[] names)
		=> source.Get(names)?.Value<bool?>();

	private static decimal? ToDecimal(this JToken value)
	{
		if (value is null)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return decimal.TryParse(value.Value<string>(),
			NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;
	}

	private static DateTime? ToDateTime(this JToken value)
	{
		if (value is null)
			return null;
		if (value.Type == JTokenType.Date)
			return value.Value<DateTime>().ToUniversalTime();
		if (value.Type == JTokenType.Integer)
		{
			var timestamp = value.Value<long>();
			return timestamp > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(timestamp)
					.UtcDateTime
				: DateTimeOffset.FromUnixTimeSeconds(timestamp)
					.UtcDateTime;
		}
		var text = value.Value<string>();
		if (long.TryParse(text, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numeric))
			return numeric > 10_000_000_000
				? DateTimeOffset.FromUnixTimeMilliseconds(numeric)
					.UtcDateTime
				: DateTimeOffset.FromUnixTimeSeconds(numeric).UtcDateTime;
		return DateTime.TryParse(text, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal, out var parsed)
			? parsed
			: null;
	}
}
