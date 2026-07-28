namespace StockSharp.Finage.Native;

static class FinageExtensions
{
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
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static string NormalizeFinageSymbol(this string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		return value
			.Replace("/", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace("_", string.Empty, StringComparison.Ordinal)
			.Replace(" ", string.Empty, StringComparison.Ordinal)
			.ToUpperInvariant();
	}

	public static FinageInstrument ToInstrument(
		this string value, string name = null)
	{
		var symbol = value.NormalizeFinageSymbol();
		var quote = symbol.Length >= 6 ? symbol[^3..] : null;
		var @base = symbol.Length >= 6 ? symbol[..^3] : symbol;
		return new()
		{
			Symbol = symbol,
			Name = name.IsEmpty(symbol),
			BaseCurrency = @base,
			QuoteCurrency = quote,
		};
	}

	public static SecurityId ToSecurityId(
		this FinageInstrument instrument)
		=> new()
		{
			SecurityCode = instrument.Symbol,
			BoardCode = BoardCodes.Finage,
			Native = instrument.Symbol,
		};

	public static SecurityMessage ToSecurityMessage(
		this FinageInstrument instrument, long transactionId)
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
			Class = SecurityTypes.Currency.ToString(),
			SecurityType = SecurityTypes.Currency,
			Currency = currency,
		};
	}

	public static FinageInstrument[] ToInstruments(this JToken token)
	{
		if (token is not JObject root ||
			root["symbols"] is not JArray symbols)
			throw new InvalidDataException(
				"Finage symbol list returned an invalid response.");

		return symbols.OfType<JObject>()
			.Select(value =>
			{
				var symbol = value.Value<string>("symbol");
				return symbol.IsEmpty()
					? null
					: symbol.ToInstrument(
						value.Value<string>("name"));
			})
			.Where(static instrument => instrument is not null)
			.DistinctBy(static instrument => instrument.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	public static FinageQuote ToQuote(this JToken token)
	{
		if (token is not JObject value)
			throw new InvalidDataException(
				"Finage last quote returned an invalid response.");

		var symbol = value.Value<string>("symbol");
		if (symbol.IsEmpty())
			throw new InvalidDataException(
				"Finage last quote does not contain a symbol.");

		return CreateQuote(value, symbol);
	}

	public static FinageBar[] ToBars(this JToken token)
	{
		if (token is not JObject root ||
			root["results"] is not JArray results)
			throw new InvalidDataException(
				"Finage aggregates returned an invalid response.");

		return results.OfType<JObject>()
			.Select(static value => new FinageBar
			{
				Time = ParseTime(value["t"]) ?? default,
				Open = Decimal(value["o"]) ?? 0,
				High = Decimal(value["h"]) ?? 0,
				Low = Decimal(value["l"]) ?? 0,
				Close = Decimal(value["c"]) ?? 0,
				Volume = Decimal(value["v"]),
			})
			.Where(static bar => bar.Time != default)
			.OrderBy(static bar => bar.Time)
			.ToArray();
	}

	public static FinageQuote ParseStreamQuote(string json)
	{
		JObject value;
		try
		{
			value = JObject.Parse(json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Finage WebSocket returned invalid JSON.", error);
		}

		if (value["status_code"] is not null)
			return null;

		var symbol = value.Value<string>("s");
		if (symbol.IsEmpty())
			return null;

		var quote = CreateQuote(value, symbol);
		return quote.Bid is null && quote.Ask is null
			? null
			: quote;
	}

	public static string BuildSubscription(
		IEnumerable<string> symbols, bool subscribe)
	{
		var value = (symbols ?? [])
			.Select(NormalizeFinageSymbol)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Join(",");

		if (value.IsEmpty())
			throw new ArgumentException(
				"At least one Finage symbol is required.",
				nameof(symbols));

		return new JObject
		{
			["action"] = subscribe ? "subscribe" : "unsubscribe",
			["symbols"] = value,
		}.ToString(Formatting.None);
	}

	public static Uri BuildStreamingUri(
		this Uri endpoint, string token)
	{
		if (endpoint is null || !endpoint.IsAbsoluteUri ||
			endpoint.Scheme != "wss")
			throw new ArgumentException(
				"Finage streaming endpoint must be an absolute WSS URI.",
				nameof(endpoint));

		token = token.ThrowIfEmpty(nameof(token)).Trim();
		var builder = new UriBuilder(endpoint);
		var values = builder.Query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Where(static value =>
				!value.StartsWith("token=",
					StringComparison.OrdinalIgnoreCase))
			.Append("token=" + Uri.EscapeDataString(token));
		builder.Query = values.Join("&");
		return builder.Uri;
	}

	public static (int Multiplier, string Unit)
		ToFinageInterval(this TimeSpan timeFrame)
	{
		if (!_timeFrames.Contains(timeFrame))
			throw new NotSupportedException(
				$"Finage candle interval '{timeFrame}' is unsupported.");

		if (timeFrame == TimeSpan.FromDays(7))
			return (1, "week");
		if (timeFrame == TimeSpan.FromDays(1))
			return (1, "day");
		if (timeFrame >= TimeSpan.FromHours(1))
			return ((int)timeFrame.TotalHours, "hour");
		return ((int)timeFrame.TotalMinutes, "minute");
	}

	private static FinageQuote CreateQuote(
		JObject value, string symbol)
		=> new()
		{
			Symbol = symbol.NormalizeFinageSymbol(),
			Time = ParseTime(value["timestamp"] ?? value["t"]) ??
				DateTime.UtcNow,
			Bid = Decimal(value["bid"] ?? value["b"] ??
				value["bp"]),
			Ask = Decimal(value["ask"] ?? value["a"] ??
				value["ap"]),
		};

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

	private static DateTime? ParseTime(JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;

		if (value.Type is JTokenType.Integer)
		{
			var number = value.Value<long>();
			try
			{
				return Math.Abs(number) >= 100000000000L
					? DateTimeOffset.FromUnixTimeMilliseconds(number)
						.UtcDateTime
					: DateTimeOffset.FromUnixTimeSeconds(number)
						.UtcDateTime;
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}

		var text = value.Value<string>();
		if (text.IsEmpty())
			return null;
		if (long.TryParse(text, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var numberValue))
			return ParseTime(new JValue(numberValue));
		return DateTime.TryParse(text, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal, out var result)
			? result
			: null;
	}
}
