namespace StockSharp.JQuants.Native;

static class JQuantsExtensions
{
	private static readonly TimeSpan _japanOffset =
		TimeSpan.FromHours(9);
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromDays(1),
	];

	public static IEnumerable<TimeSpan> TimeFrames => _timeFrames;

	public static bool IsIntraday(this TimeSpan value)
		=> value < TimeSpan.FromDays(1) &&
			_timeFrames.Contains(value);

	public static JQuantsInstrument ToEquity(this JObject value)
		=> new()
		{
			Code = value.String("Code"),
			Name = value.String("CoName"),
			EnglishName = value.String("CoNameEn"),
			Market = value.String("Mkt"),
			MarketName = value.String("MktNm"),
			Sector = value.String("S33").IsEmpty(
				value.String("S17")),
			SectorName = value.String("S33Nm").IsEmpty(
				value.String("S17Nm")),
			Kind = JQuantsInstrumentKinds.Equity,
		};

	public static JQuantsInstrument ToDerivative(this JObject value,
		JQuantsInstrumentKinds kind)
	{
		var putCall = value.String("PCDiv",
			"PutCallDivision");
		return new()
		{
			Code = value.String("Code"),
			Name = value.String("ProdCat",
				"ProductCategory"),
			EnglishName = value.String("ProdCat",
				"ProductCategory"),
			Market = "DERIVATIVES",
			MarketName = "JPX Derivatives",
			Sector = value.String("ProdCat",
				"ProductCategory"),
			SectorName = value.String("ProdCat",
				"ProductCategory"),
			Kind = kind,
			ProductCategory = value.String("ProdCat",
				"ProductCategory"),
			Underlying = value.String("UndSSO",
				"UnderlyingSecuritiesCode"),
			Strike = value.Decimal("Strike"),
			OptionType = kind == JQuantsInstrumentKinds.Option
				? putCall?.ToUpperInvariant() switch
				{
					"1" or "C" or "CALL" => OptionTypes.Call,
					"2" or "P" or "PUT" => OptionTypes.Put,
					_ => null,
				}
				: null,
			Expiry = value.Get("LTD", "LastTradingDay",
				"SQD", "SpecialQuotationDay").ToJQuantsDate(),
		};
	}

	public static JQuantsBar ToBar(this JObject value,
		bool intraday)
	{
		var date = value.String("Date");
		var time = intraday
			? value.String("Time")
			: "00:00:00";
		return new()
		{
			Code = value.String("Code"),
			Time = ToJQuantsTime(date, time),
			Open = value.Decimal("O", "Open") ?? 0,
			High = value.Decimal("H", "High") ?? 0,
			Low = value.Decimal("L", "Low") ?? 0,
			Close = value.Decimal("C", "Close") ?? 0,
			Volume = value.Decimal("Vo", "Volume") ?? 0,
			OpenInterest = value.Decimal("OI", "OpenInterest"),
		};
	}

	public static JQuantsTrade[] ParseTrades(string csv,
		string requestedCode)
	{
		if (csv.IsEmpty())
			return [];
		var lines = csv.Split(
			['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
		if (lines.Length < 2)
			return [];
		var headers = ParseCsvLine(lines[0])
			.Select(NormalizeHeader)
			.ToArray();
		var result = new List<JQuantsTrade>(lines.Length - 1);
		foreach (var line in lines.Skip(1))
		{
			var fields = ParseCsvLine(line);
			string Get(params string[] names)
			{
				foreach (var name in names)
				{
					var index = Array.IndexOf(headers,
						NormalizeHeader(name));
					if (index >= 0 && index < fields.Length &&
						!fields[index].IsEmpty())
						return fields[index].Trim();
				}
				return null;
			}
			var code = Get("Code", "SecurityCode",
				"IssueCode");
			if (code.IsEmpty() ||
				!requestedCode.IsEmpty() &&
				!code.EqualsIgnoreCase(requestedCode))
				continue;
			var price = Get("Price", "P",
				"TradePrice").ToJQuantsDecimal() ?? 0;
			var volume = Get("Volume", "Vo", "Quantity",
				"TradeVolume").ToJQuantsDecimal() ?? 0;
			var time = ToJQuantsTime(Get("Date",
				"TradeDate"), Get("Time", "TradeTime",
				"Timestamp"));
			if (price <= 0 || volume <= 0 || time == default)
				continue;
			result.Add(new()
			{
				Code = code,
				Id = Get("Id", "TradeId", "Sequence",
					"SequentialTradeNumber"),
				Time = time,
				Price = price,
				Volume = volume,
			});
		}
		return [.. result];
	}

	public static JQuantsBar[] Aggregate(
		IEnumerable<JQuantsBar> source, TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromMinutes(1))
			return source.OrderBy(static bar => bar.Time).ToArray();
		if (!timeFrame.IsIntraday())
			throw new NotSupportedException(
				$"Cannot aggregate J-Quants bars to '{timeFrame}'.");
		return source
			.GroupBy(bar => new
			{
				bar.Code,
				Bucket = Bucket(bar.Time, timeFrame),
			})
			.Select(static group =>
			{
				var values = group.OrderBy(static bar => bar.Time)
					.ToArray();
				return new JQuantsBar
				{
					Code = group.Key.Code,
					Time = group.Key.Bucket,
					Open = values[0].Open,
					High = values.Max(static bar => bar.High),
					Low = values.Min(static bar => bar.Low),
					Close = values[^1].Close,
					Volume = values.Sum(static bar => bar.Volume),
				};
			})
			.OrderBy(static bar => bar.Time)
			.ToArray();
	}

	private static DateTimeOffset Bucket(DateTimeOffset value,
		TimeSpan timeFrame)
	{
		var local = value.ToOffset(_japanOffset);
		var ticks = local.TimeOfDay.Ticks /
			timeFrame.Ticks * timeFrame.Ticks;
		return new(local.Date.AddTicks(ticks), _japanOffset);
	}

	public static DateTime? ToJQuantsDate(this JToken value)
	{
		var time = ToJQuantsTime(value?.Value<string>(), null);
		return time == default
			? null
			: DateTime.SpecifyKind(time.Date, DateTimeKind.Utc);
	}

	public static DateTimeOffset ToJQuantsTime(string date,
		string time)
	{
		if (date.IsEmpty() && time.IsEmpty())
			return default;
		var text = date.IsEmpty()
			? time
			: time.IsEmpty()
				? date
				: $"{date} {time}";
		var formats = new[]
		{
			"yyyy-MM-dd HH:mm:ss.fff",
			"yyyy-MM-dd HH:mm:ss",
			"yyyy-MM-dd HH:mm",
			"yyyyMMdd HHmmssfff",
			"yyyyMMdd HHmmss",
			"yyyyMMdd HH:mm:ss",
			"yyyy-MM-dd",
			"yyyyMMdd",
		};
		if (DateTime.TryParseExact(text.Trim(), formats,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var value))
			return new(DateTime.SpecifyKind(value,
				DateTimeKind.Unspecified), _japanOffset);
		if (DateTimeOffset.TryParse(text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AllowWhiteSpaces, out var offset))
			return offset;
		return default;
	}

	public static decimal? ToJQuantsDecimal(this string value)
	{
		if (value.IsEmpty())
			return null;
		return decimal.TryParse(value.Replace(",",
			string.Empty, StringComparison.Ordinal),
			NumberStyles.Any, CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;
	}

	public static decimal? ToJQuantsDecimal(this JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		if (value.Type is JTokenType.Integer or JTokenType.Float)
			return value.Value<decimal>();
		return value.Value<string>().ToJQuantsDecimal();
	}

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
		=> value.Get(names).ToJQuantsDecimal();

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
}
