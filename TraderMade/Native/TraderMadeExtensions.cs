namespace StockSharp.TraderMade.Native;

static class TraderMadeExtensions
{
	private static readonly HashSet<string> _crypto =
		new(StringComparer.OrdinalIgnoreCase)
		{
			"ADA", "ATOM", "AVAX", "BCH", "BNB", "BTC", "BTG",
			"DAI", "DASH", "DOGE", "DOT", "EGLD", "ETH", "LINK",
			"LTC", "MATIC", "SOL", "TRX", "UNI", "USDC", "USDT",
			"XLM", "XMR", "XRP",
		};

	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromDays(1),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static string NormalizeTraderMadeSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		var suffix = value.IndexOf(':');
		if (suffix >= 0)
			value = value[..suffix];
		return value.Replace("/", string.Empty,
				StringComparison.Ordinal)
			.Replace("-", string.Empty,
				StringComparison.Ordinal)
			.Replace("_", string.Empty,
				StringComparison.Ordinal)
			.ToUpperInvariant();
	}

	public static TraderMadeInstrument ToInstrument(
		this string value, string name = null)
	{
		var symbol = value.NormalizeTraderMadeSymbol();
		var quote = symbol.Length >= 6 ? symbol[^3..] : null;
		var @base = symbol.Length >= 6 ? symbol[..^3] : symbol;
		var type = symbol.Length == 6
			? _crypto.Contains(@base) || _crypto.Contains(quote)
				? SecurityTypes.CryptoCurrency
				: SecurityTypes.Currency
			: SecurityTypes.Cfd;
		return new()
		{
			Symbol = symbol,
			BaseCurrency = @base,
			QuoteCurrency = quote,
			Name = name.IsEmpty(symbol),
			SecurityType = type,
		};
	}

	public static SecurityId ToSecurityId(
		this TraderMadeInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = BoardCodes.TraderMade,
			Native = instrument.Symbol,
		};

	public static SecurityMessage ToSecurityMessage(
		this TraderMadeInstrument instrument, long transactionId)
	{
		CurrencyTypes? currency = null;
		if (!instrument.QuoteCurrency.IsEmpty() &&
			Enum.TryParse<CurrencyTypes>(instrument.QuoteCurrency,
				true, out var parsed))
			currency = parsed;
		return new()
		{
			OriginalTransactionId = transactionId,
			SecurityId = instrument.ToSecurityId(),
			Name = instrument.Name,
			ShortName = instrument.Symbol,
			Class = instrument.SecurityType.ToString(),
			SecurityType = instrument.SecurityType,
			Currency = currency,
		};
	}

	public static Dictionary<string, string> ToCurrencies(
		this JToken token)
	{
		if (token is not JObject root ||
			root["available_currencies"] is not JObject currencies)
			throw new InvalidDataException(
				"TraderMade currency list returned an invalid response.");
		return currencies.Properties()
			.Where(static property => !property.Name.IsEmpty())
			.ToDictionary(
				static property => property.Name.ToUpperInvariant(),
				static property =>
					property.Value.Value<string>()
						.IsEmpty(property.Name),
				StringComparer.OrdinalIgnoreCase);
	}

	public static TraderMadeQuote[] ToLiveQuotes(this JToken token)
	{
		if (token is not JObject root ||
			root["quotes"] is not JArray quotes)
			throw new InvalidDataException(
				"TraderMade live endpoint returned an invalid response.");
		var time = FromUnix(root.Value<long?>("timestamp")) ??
			ParseTime(root.Value<string>("requested_time")) ??
			DateTime.UtcNow;
		return quotes.OfType<JObject>()
			.Select(value =>
			{
				var symbol = value.Value<string>("instrument");
				if (symbol.IsEmpty())
					symbol =
						value.Value<string>("base_currency") +
						value.Value<string>("quote_currency");
				return CreateQuote(value, symbol, time);
			})
			.Where(static quote => !quote.Symbol.IsEmpty())
			.ToArray();
	}

	public static TraderMadeBar[] ToBars(this JToken token)
	{
		if (token is not JObject root ||
			root["quotes"] is not JArray quotes)
			throw new InvalidDataException(
				"TraderMade timeseries returned an invalid response.");
		return quotes.OfType<JObject>()
			.Select(value => new TraderMadeBar
			{
				Time = ParseTime(value.Value<string>("date")
					.IsEmpty(value.Value<string>("date_time"))
					.IsEmpty(value.Value<string>("timestamp"))) ??
						default,
				Open = Decimal(value["open"]) ?? 0,
				High = Decimal(value["high"]) ?? 0,
				Low = Decimal(value["low"]) ?? 0,
				Close = Decimal(value["close"]) ?? 0,
			})
			.Where(static bar => bar.Time != default)
			.OrderBy(static bar => bar.Time)
			.ToArray();
	}

	public static TraderMadeQuote ParseStreamQuote(string json)
	{
		JObject value;
		try
		{
			value = JObject.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"TraderMade WebSocket returned invalid JSON.", error);
		}
		var type = value.Value<string>("t");
		if (type is not ("QUOTE" or "LAST_QUOTE"))
			return null;
		var symbol = value.Value<string>("s");
		var time = ParseTime(value.Value<string>("ts")) ??
			DateTime.UtcNow;
		var quote = CreateQuote(value, symbol, time);
		if (value["ladder"] is JObject ladder)
			quote = new()
			{
				Symbol = quote.Symbol,
				Time = quote.Time,
				Bid = quote.Bid,
				Ask = quote.Ask,
				Mid = quote.Mid,
				BidVolume = quote.BidVolume,
				AskVolume = quote.AskVolume,
				Bids = ParseLadder(ladder["b"]),
				Asks = ParseLadder(ladder["a"]),
			};
		return quote;
	}

	public static string BuildLogin(string key, bool ladder)
		=> new JObject
		{
			["action"] = "login",
			["key"] = key.ThrowIfEmpty(nameof(key)),
			["fmt"] = "JSON",
			["send_ladder"] = ladder,
		}.ToString(Formatting.None);

	public static string BuildSubscription(
		IEnumerable<string> symbols, bool subscribe)
	{
		var values = (symbols ?? [])
			.Select(NormalizeTraderMadeSymbol)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(static symbol => symbol + ":QUOTE")
			.ToArray();
		if (values.Length == 0)
			throw new ArgumentException(
				"At least one TraderMade symbol is required.",
				nameof(symbols));
		var result = new JObject
		{
			["action"] = subscribe ? "subscribe" : "unsubscribe",
			["symbols"] = new JArray(values),
		};
		if (subscribe)
			result["send_last"] = true;
		return result.ToString(Formatting.None);
	}

	public static (string Interval, int Period, TimeSpan MaxRange)
		ToTraderMadeInterval(this TimeSpan timeFrame)
	{
		if (!_timeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"TraderMade candle interval '{timeFrame}' is " +
					"unsupported.");
		if (timeFrame == TimeSpan.FromDays(1))
			return ("daily", 1, TimeSpan.FromDays(366));
		if (timeFrame >= TimeSpan.FromHours(1))
			return ("hourly", (int)timeFrame.TotalHours,
				TimeSpan.FromDays(31));
		return ("minute", (int)timeFrame.TotalMinutes,
			TimeSpan.FromDays(2));
	}

	private static TraderMadeQuote CreateQuote(JObject value,
		string symbol, DateTime time)
	{
		var bid = Decimal(value["b"] ?? value["bid"]);
		var ask = Decimal(value["a"] ?? value["ask"]);
		var bidVolume = Decimal(value["bv"] ??
			value["bid_volume"]);
		var askVolume = Decimal(value["av"] ??
			value["ask_volume"]);
		var mid = Decimal(value["m"] ?? value["mid"]) ??
			(bid is not null && ask is not null
				? (bid + ask) / 2
				: bid ?? ask);
		return new()
		{
			Symbol = symbol?.NormalizeTraderMadeSymbol(),
			Time = time,
			Bid = bid,
			Ask = ask,
			Mid = mid,
			BidVolume = bidVolume,
			AskVolume = askVolume,
			Bids = bid is null
				? []
				: [new(bid.Value, bidVolume ?? 0)],
			Asks = ask is null
				? []
				: [new(ask.Value, askVolume ?? 0)],
		};
	}

	private static QuoteChange[] ParseLadder(JToken token)
		=> token is not JArray levels
			? []
			: levels.OfType<JArray>()
				.Where(static level => level.Count >= 2)
				.Select(static level => new QuoteChange(
					Decimal(level[0]) ?? 0,
					Decimal(level[1]) ?? 0))
				.Where(static quote => quote.Price > 0)
				.ToArray();

	private static decimal? Decimal(JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return decimal.TryParse(value.Value<string>(),
			NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;
	}

	private static DateTime? FromUnix(long? value)
	{
		if (value is null)
			return null;
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(value.Value)
				.UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}

	private static DateTime? ParseTime(string value)
	{
		if (value.IsEmpty())
			return null;
		var formats = new[]
		{
			"yyyyMMdd-HH:mm:ss.fff",
			"yyyy-MM-dd-HH:mm",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd HH:mm",
			"yyyy-MM-dd",
			"ddd, dd MMM yyyy HH:mm:ss 'GMT'",
		};
		return DateTime.TryParseExact(value.Trim(), formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
				: DateTime.TryParse(value,
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal |
						DateTimeStyles.AdjustToUniversal,
					out result)
					? result
					: null;
	}
}
